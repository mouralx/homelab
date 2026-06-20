FROM ubuntu:latest

WORKDIR /llmster

EXPOSE 1234

ENV PATH="/llmster/.lmstudio/bin:${PATH}"

RUN apt update && apt upgrade -y && apt install curl libatomic1 libgomp1 -y

RUN curl -fsSL https://lmstudio.ai/install.sh | bash

RUN lms server start --bind 0.0.0.0 --port 1234

CMD tail -f /dev/null