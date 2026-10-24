ui = true
disable_mlock = true
cluster_addr = "https://vault:8201"

storage "raft" {
  path    = "/vault/data"
  node_id = "vault"
}

# HTTPS terminates at Nginx Proxy Manager; do not publish this listener to the LAN.
listener "tcp" {
  address     = "0.0.0.0:8200"
  tls_disable = true
}
