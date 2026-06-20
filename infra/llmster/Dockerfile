FROM ubuntu:latest

EXPOSE 1234

ENV PATH="/root/.lmstudio/bin:${PATH}"

RUN apt update -qq && apt install curl libatomic1 libgomp1 watch -y -qq

RUN curl -fsSL https://lmstudio.ai/install.sh | bash -s -- --quiet

RUN lms server start --bind *.*.*.* --port 1234

ENTRYPOINT [ "lms", "log", "stream" ]