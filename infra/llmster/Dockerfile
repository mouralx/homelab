FROM ubuntu:latest

EXPOSE 1234

ENV PATH="/root/.lmstudio/bin:${PATH}"

RUN apt update && apt upgrade -y && apt install curl libatomic1 -y

RUN curl -fsSL https://lmstudio.ai/install.sh | bash

RUN lms get google/gemma-4-12b

ENTRYPOINT [ "lms", "log", "stream" ]