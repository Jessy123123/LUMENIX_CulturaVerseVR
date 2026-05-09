from flask import Flask, request, jsonify
from flask_cors import CORS
import requests

app = Flask(__name__)
CORS(app)

OLLAMA_URL = "http://localhost:11434/api/generate"

@app.route("/chat", methods=["POST"])
def chat():
    data = request.json
    prompt = data.get("prompt", "")

    response = requests.post(
        OLLAMA_URL,
        json={
            "model": "qwen2.5:1.5b",
            "prompt": prompt,
            "stream": False
        }
    )

    result = response.json()

    return jsonify({
        "response": result.get("response", "")
    })

if __name__ == "__main__":
    app.run(host="0.0.0.0", port=5000)