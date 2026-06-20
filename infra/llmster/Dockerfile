FROM ubuntu:latest

ENV PATH="/root/.lmstudio/bin:${PATH}"

RUN apt update -qq && apt install curl libatomic1 libgomp1 watch -y -qq

RUN curl -fsSL https://lmstudio.ai/install.sh | bash -s -- --quiet

CMD lms server start

ENTRYPOINT [ "lms", "log", "stream" ]