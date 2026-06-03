FROM ubuntu:latest

ENV TRANSMISSION_USER="transmission" TRANSMISSION_PASSWORD="transmission"

RUN apt-get update && apt-get install -y transmission-daemon && rm -rf /var/lib/apt/lists/*

RUN mkdir -p /downloads /incomplete

EXPOSE 9091 51413 51413/udp

ENTRYPOINT [ "/bin/sh", "-c", "transmission-daemon --foreground --download-dir /downloads --incomplete-dir /incomplete --allowed \"*.*.*.*\" --user \"$TRANSMISSION_USER\" --password \"$TRANSMISSION_PASSWORD\"" ]
