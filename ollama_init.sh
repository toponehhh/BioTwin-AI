#!/bin/bash
# Initialize Ollama with required models

echo "Waiting for Ollama to be ready..."
for i in {1..30}; do
  if curl -s http://localhost:11434/api/tags > /dev/null 2>&1; then
    echo "Ollama is ready!"
    break
  fi
  echo "Attempt $i: Waiting for Ollama..."
  sleep 2
done

echo "Pulling nomic-embed-text model..."
ollama pull nomic-embed-text

echo "Pulling gemma4:e2b model..."
ollama pull gemma4:e2b

echo "Models initialized successfully!"
