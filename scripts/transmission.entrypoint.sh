apt update -yq

apt install -yq transmission-daemon 

rm -rf /var/lib/apt/lists/*

mkdir -p /downloads /incomplete

transmission-daemon \
    -f -a $TRANSMISSION_ALLOWED \
    --auth -u $TRANSMISSION_USER \
    --password $TRANSMISSION_PASSWORD \
    --incomplete-dir /incomplete \
    -w /downloads