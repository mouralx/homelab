FROM ubuntu:latest

ENV TRANSMISSION_USER="transmission" TRANSMISSION_PASSWORD="transmission" TRANSMISSION_ALLOWED="*.*.*.*"

RUN apt-get update && apt-get install -y transmission-daemon && rm -rf /var/lib/apt/lists/*

RUN mkdir -p /downloads /incomplete

EXPOSE 9091 51413 51413/udp

ENTRYPOINT [ "transmission-daemon", "-f", "-a", "${ALL_PROXY}", "-u", "${TRANSMISSION_USER}", "-p", "${TRANSMISSION_PASSWORD}", "--incomplete-dir", "/incomplete", "-w", "/downloads" ]
