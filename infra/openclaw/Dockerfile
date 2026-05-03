FROM ubuntu:resolute

EXPOSE 18789

RUN apt update && apt upgrade -y && apt install curl -y

RUN curl -fsSL https://openclaw.ai/install.sh | bash -s -- --no-onboard

RUN openclaw setup

ENTRYPOINT [ "openclaw", "gateway", "--auth", "none" ]