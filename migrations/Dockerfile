FROM migrate/migrate:v4.19.1 AS migrate

FROM alpine:3.23.3

RUN apk add --no-cache wget

COPY --from=migrate /usr/local/bin/migrate /usr/local/bin/migrate
COPY scripts /migrations
COPY entrypoint.sh /entrypoint.sh

USER 1000

ENTRYPOINT ["/entrypoint.sh"]
