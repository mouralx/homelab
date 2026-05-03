FROM ubuntu:resolute

EXPOSE 18789

ENV OPENCLAW_TOKEN="d16cc8d764393089fecead85cbead8c2ffc4d8635a7a8ab3"
ENV OPENCLAW_ORIGIN="https://openclaw.mouras.me"

RUN apt update && apt upgrade -y && apt install curl jq -y
RUN curl -fsSL https://openclaw.ai/install.sh | bash -s -- --no-onboard
RUN openclaw setup
RUN jq --arg v "$OPENCLAW_TOKEN" '.gateway.auth.token = $v' /root/.openclaw/openclaw.json > tmp.json && mv tmp.json /root/.openclaw/openclaw.json
RUN jq --arg v "$OPENCLAW_ORIGIN" '.gateway.controlUi.allowedOrigins += [$v]' /root/.openclaw/openclaw.json > tmp.json && mv tmp.json /root/.openclaw/openclaw.json

ENTRYPOINT [ "/bin/sh", "-c", "exec openclaw gateway --auth=token --token=$OPENCLAW_TOKEN" ]