from flask import Flask, render_template, request, make_response, g
from redis import Redis
import os
import socket
import random
import json
import logging
import dask.dataframe as dd
from collections import defaultdict
from dask.distributed import Client
import time

option_a = os.getenv('OPTION_A', "Cats")
option_b = os.getenv('OPTION_B', "Dogs")
hostname = socket.gethostname()

app = Flask(__name__)

gunicorn_error_logger = logging.getLogger('gunicorn.error')
app.logger.handlers.extend(gunicorn_error_logger.handlers)
app.logger.setLevel(logging.INFO)

def get_redis():
    if not hasattr(g, 'redis'):
        g.redis = Redis(host="redis", db=0, socket_timeout=5)
    return g.redis

def load_data(ratings_file, movies_file):
    ratings = dd.read_csv(ratings_file, sep='::', header=None,
                          names=['UserID', 'MovieID', 'Rating', 'Timestamp'],
                          engine='python', encoding='ISO-8859-1')
    movies = dd.read_csv(movies_file, sep='::', header=None,
                         names=['MovieID', 'Title', 'Genres'],
                         engine='python', encoding='ISO-8859-1')
    return ratings, movies

def manhattan_distance(user1, user2):
    common_movies = set(user1.keys()) & set(user2.keys())
    return sum(abs(user1[movie] - user2[movie]) for movie in common_movies)

def find_similar_users(target_user, user_movie_ratings, n=5):
    distances = []
    target_ratings = user_movie_ratings[target_user]
    for user, ratings in user_movie_ratings.items():
        if user != target_user:
            distance = manhattan_distance(target_ratings, ratings)
            distances.append((user, distance))
    distances.sort(key=lambda x: x[1])
    return [user for user, _ in distances[:n]]

def get_recommendations(target_user, similar_users, user_movie_ratings, n=5):
    recommendations = defaultdict(float)
    target_ratings = user_movie_ratings[target_user]
    for user in similar_users:
        for movie, rating in user_movie_ratings[user].items():
            if movie not in target_ratings:
                recommendations[movie] += rating
    recommendations = sorted(recommendations.items(), key=lambda x: x[1], reverse=True)
    return recommendations[:n]

def get_movie_titles(movies, recommendations):
    movies_pd = movies.compute()
    movie_titles = []
    for movie_id, score in recommendations:
        movie_data = movies_pd[movies_pd['MovieID'] == movie_id]
        if not movie_data.empty:
            movie_title = movie_data['Title'].iloc[0]
            movie_titles.append(f"{movie_title}")
        else:
            movie_titles.append(f"ID de película {movie_id} (Título no encontrado)")
    return movie_titles

@app.route("/", methods=['POST', 'GET'])
def hello():
    voter_id = request.cookies.get('voter_id')
    input_voter_id = request.form.get('voter_id')

    if input_voter_id:
        voter_id = input_voter_id
    elif not voter_id:
        voter_id = hex(random.getrandbits(64))[2:-1]

    vote = None
    movie_titles = []  # Variable para almacenar las recomendaciones

    if request.method == 'POST' and 'vote' in request.form:
        redis = get_redis()
        vote = request.form['vote']
        app.logger.info('Received vote for %s from user %s', vote, voter_id)
        data = json.dumps({'voter_id': voter_id, 'vote': vote})
        redis.rpush('votes', data)

        # Procesar las recomendaciones
        client = Client()
        ratings, movies = load_data('ml-1m/ratings.dat', 'ml-1m/movies.dat')
        user_movie_ratings = ratings.groupby('UserID').apply(
            lambda df: dict(zip(df['MovieID'], df['Rating'])),
            meta=('x', 'object')
        ).compute()

        target_user = int(voter_id, 16)  # Convertir el voter_id hexadecimal a entero
        similar_users = find_similar_users(target_user, user_movie_ratings)
        recommendations = get_recommendations(target_user, similar_users, user_movie_ratings)
        movie_titles = get_movie_titles(movies, recommendations)

        # Almacenar las recomendaciones en Redis (por si es necesario en el futuro)
        recommendation_data = json.dumps({'voter_id': voter_id, 'recommendations': movie_titles})
        redis.rpush('recommendations', recommendation_data)

        client.close()

    # Renderizar la plantilla y pasar las recomendaciones
    resp = make_response(render_template(
        'index.html',
        option_a=option_a,
        option_b=option_b,
        hostname=hostname,
        vote=vote,
        voter_id=voter_id,
        recommendations=movie_titles  # Pasar las recomendaciones a la plantilla
    ))
    resp.set_cookie('voter_id', voter_id)
    return resp

if __name__ == "__main__":
    app.run(host='0.0.0.0', port=80, debug=True, threaded=True)