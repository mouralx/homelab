FROM ubuntu:latest

ENV TRANSMISSION_USER="transmission" TRANSMISSION_PASSWORD="transmission" TRANSMISSION_ALLOWED="10.51.0.2"

RUN apt-get update && apt-get install -y transmission-daemon && rm -rf /var/lib/apt/lists/*

RUN mkdir -p /downloads /incomplete

EXPOSE 9091 51413 51413/udp

ENTRYPOINT [ "transmission-daemon", "-f", "-a", "*.*.*.*", "-u", "\"${TRANSMISSION_USER}\"", "-p", "\"${TRANSMISSION_PASSWORD}\"", "--incomplete-dir", "/incomplete", "-w", "/downloads" ]
