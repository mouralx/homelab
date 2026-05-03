FROM ubuntu:resolute

EXPOSE 18789

ENV OPENCLAW_TOKEN="d16cc8d764393089fecead85cbead8c2ffc4d8635a7a8ab3"

RUN apt update && apt upgrade -y && apt install curl -y

RUN curl -fsSL https://openclaw.ai/install.sh | bash -s -- --no-onboard

RUN openclaw setup

ENTRYPOINT [ "openclaw", "gateway", "--auth", "token", "--token", $OPENCLAW_TOKEN ]