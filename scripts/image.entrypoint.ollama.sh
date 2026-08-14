#!/bin/bash

# Start the ollama server, then ensure the Honcho embedding model is pulled.
# Idempotent: `ollama pull` is a no-op when the model is already present.

echo "Starting ollama server..."
ollama serve &

SERVER_PID=$!

sleep 5

echo "Ensuring embedding model nomic-embed-text is pulled..."
ollama pull nomic-embed-text

ollama list

wait $SERVER_PID
