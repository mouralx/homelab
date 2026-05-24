FROM ubuntu:24.04

ENV PATH="/root/.lmstudio/bin:${PATH}"

EXPOSE 1234

RUN apt update && apt install -y curl ca-certificates

RUN rm -rf /var/lib/apt/lists/*

RUN curl -fsSL https://lmstudio.ai/install.sh | bash

RUN lms daemon up

ENTRYPOINT [ "lms", "server", "start" ]