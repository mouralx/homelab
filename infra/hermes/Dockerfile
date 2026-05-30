FROM ubuntu:latest

EXPOSE 9119
EXPOSE 8642

RUN apt update
RUN apt install -y curl
RUN curl -fsSL https://raw.githubusercontent.com/NousResearch/hermes-agent/main/scripts/install.sh | bash
RUN 