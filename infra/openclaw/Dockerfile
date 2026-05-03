FROM ubuntu:resolute

EXPOSE 18789

ENV OPENCLAW_AUTO_APPROVE_DEVICES=true
ENV OPENCLAW_GATEWAY_AUTH_MODE=token
ENV OPENCLAW_GATEWAY_AUTH_TOKEN=8d2a495fccb093677ad9a6f90f5a961e11d61006837cb854
ENV OPENCLAW_GATEWAY_CONTROLUI_ALLOWEDORIGINS=openclaw.mouras.me

RUN apt update && apt upgrade -y && apt install curl -y

RUN curl -fsSL https://openclaw.ai/install.sh | bash -s -- --no-onboard

RUN openclaw setup

ENTRYPOINT [ "openclaw", "gateway" ]