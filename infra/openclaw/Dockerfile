FROM ubuntu:resolute

EXPOSE 18789

ENV OPENCLAW_TOKEN="d16cc8d764393089fecead85cbead8c2ffc4d8635a7a8ab3"
ENV OPENCLAW_ORIGIN="https://openclaw.mouras.me"
ENV OPENCLAW_ALLOW_INSECURE_AUTH="true"

RUN apt update && apt upgrade -y && apt install curl jq -y
RUN curl -fsSL https://openclaw.ai/install.sh | bash -s -- --no-onboard
#RUN openclaw setup
#RUN rm /root/.openclaw/openclaw.json
COPY ./openclaw.json /root/.openclaw/openclaw.json

ENTRYPOINT [ "openclaw", "gateway", "--auth", "trusted-proxy", "--allow-unconfigured" ]