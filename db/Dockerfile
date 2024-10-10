# Usa la imagen oficial de PostgreSQL como base
FROM postgres:15-alpine

# Configura variables de entorno para PostgreSQL
ENV POSTGRES_USER=postgres
ENV POSTGRES_PASSWORD=postgres
ENV POSTGRES_DB=votingapp

# Exponer el puerto de PostgreSQL
EXPOSE 5432

# Opcional: un punto de entrada personalizado o alguna configuración extra
CMD ["postgres"]