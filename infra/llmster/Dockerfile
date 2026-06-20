FROM ubuntu:latest

EXPOSE 1234

ENV PATH="/root/.lmstudio/bin:${PATH}"

RUN apt update && apt install curl libatomic1 libgomp1 watch -y

RUN curl -fsSL https://lmstudio.ai/install.sh | bash

RUN lms server start --bind 0.0.0.0 --port 1234

ENTRYPOINT [ "watch -n 1" ]