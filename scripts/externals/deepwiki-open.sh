git clone https://github.com/AsyncFuncAI/deepwiki-open.git

docker build ./deepwiki-open -t ghcr.io/home-lab/deepwiki-open:latest

echo $GH_PAT | docker login ghcr.io -u $GH_USER --password-stdin

docker push ghcr.io/home-lab/deepwiki-open:latest

rm -rf deepwiki-open