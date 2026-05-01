FROM ubuntu:resolute

EXPOSE 18789

ARG OPENCLAW_PASSWORD=4nic8ers

RUN apt update && apt upgrade -y && apt install curl -y

RUN curl -fsSL https://openclaw.ai/install.sh | bash -s -- --no-onboard

RUN openclaw setup

RUN openclaw dashboard

ENTRYPOINT [ "openclaw", "gateway", "--auth", "password", "--token", "$OPENCLAW_PASSWORD" ]
