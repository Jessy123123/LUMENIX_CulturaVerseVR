# 🏛️ CulturaVerse: Immersive Cultural VR Experience

[![Unity 6](https://img.shields.io/badge/Unity-6-black.svg?style=flat&logo=unity)](https://unity.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Ollama](https://img.shields.io/badge/LLM-Ollama%20qwen2.5%3A1.5b-blue.svg)](https://ollama.com/)
[![Google Cloud](https://img.shields.io/badge/Google%20Cloud-STT%20%2F%20TTS-4285F4.svg?logo=google-cloud)](https://cloud.google.com/)

**CulturaVerse** is a state-of-the-art Virtual Reality (VR) educational platform designed to breathe life into historical and legendary cultural figures. Built with **Unity 6**, the project leverages local LLMs and cloud-based AI services to create a bilingual, emotionally-aware interaction between users and history.

---

## 🏆 Competition Details
- **Competition:** China International College Students' Innovation Competition (2026) **Middle East Regional Competition**
- **University:** Universiti Teknologi PETRONAS (UTP)
- **Group Name:** LUMENIX
- **Project Name:** CulturaVerse

---

## 📖 Project Introduction
CulturaVerse transforms cultural education into an immersive dialogue. Traditional history books are replaced by actual conversations with AI-powered & multi-languages supported NPC characters in beautifully rendered 3D environments.

### Prototype Featured Characters:
1.  **👸 Puteri Gunung Ledang**: A legendary Malay princess from classic literature. Known for setting seven impossible conditions for her marriage, she represents the grace, wisdom, and mystery of Malay heritage.
2.  **🛡️ Yue Fei (岳飞)**: A legendary patriotic general and poet from the Chinese Southern Song Dynasty. Renowned for his unwavering loyalty and his famous poem *Man Jiang Hong* (满江红), he symbolizes courage and integrity.

Users can speak to these figures in **Malay, English, or Mandarin Chinese**, receiving historically accurate, in-character voiced responses while the surrounding environment reacts dynamically to their emotions.

---

## ✨ Key Features
- **🎙️ Multilingual Voice Interaction**: Full support for natural conversations in Malay, English, and Mandarin Chinese.
- **🧠 Local LLM Intelligence**: AI NPC logic powered by **Ollama (qwen2.5:1.5b)** for fast, private, and localized responses.
- **👂 Automatic Speech Recognition**: Integration with **Google Cloud Speech-to-Text** with real-time language detection.
- **🗣️ Dynamic Character Voices**: High-fidelity voices via **Google Cloud Text-to-Speech** (Female standard for Puteri, Male noble for Yue Fei).
- **🎭 Emotion-Aware NPCs**: Real-time emotion classification (Happy, Sad, Angry, Fearful, Surprised, Neutral) using **Hugging Face NLP models**.
- **⛈️ Reactive Environments**: The 3D world shifts based on the user's feelings such as lighting fades during sadness, turns red during moments of anger.
- **🎬 Animated Storytelling**: Custom animations for idle and talking states managed via the **Unity Animator** for realistic presence.
- **⚙️ Config-Driven Architecture**: centralized `config.json` system ensures easy deployment and security for API tokens.

---

## 🛠️ Tech Stack
| Component | Technology |
| :--- | :--- |
| **Engine** | Unity 6 (URP) |
| **Logic** | C# (.NET) |
| **LLM** | Ollama (Model: `qwen2.5:1.5b`) |
| **Speech-to-Text** | Google Cloud STT API |
| **Text-to-Speech** | Google Cloud TTS API |
| **Emotion Analysis** | Hugging Face Inference API (`distilroberta-base`) |
| **Visuals** | Unity Particle System, Shader Graph, Unity Animator |
| **Data** | JSON-based StreamingAssets config system |

---

## 🏗️ System Architecture
The CulturaVerse pipeline is designed for low-latency, emotionally synchronized interaction:

```text
[ USER SPEECH ]
       │
       ▼
 🎤 Microphone Capture (Unity)
       │
       ▼
 ☁️ Google Cloud STT ─────► [ TRANSCRIBED TEXT + LANGUAGE DETECTION ]
                             │
       ┌─────────────────────┴─────────────────────┐
       ▼                                           ▼
 🤖 Ollama (Local LLM)                     🧠 Hugging Face NLP
 [ AI-Character Reply ]                   [ Emotion & Keywords Detection ]
       │                                           │
       ▼                                           ▼
 ☁️ Google Cloud TTS                       ⚡ Unity Environment Mgr
 [ Voice Synthesis ]                      [ Real-time Weather/Lighting ]
       │                                           │
       └─────────────────────┬─────────────────────┘
                             ▼
              [ IMMERSIVE VR FEEDBACK ]
               (Voiced Reply + Animation + 
                Environmental Atmosphere)
```

---

## 🚀 How to Run

### 1. Prerequisites
- **Unity 6** (or later) installed via Unity Hub.
- **Ollama** installed on your local machine.
- **Google Cloud Platform** account with Speech-to-Text and Text-to-Speech APIs enabled.
- **Hugging Face** account to obtain an Inference API token.

### 2. Setting Up Ollama
```bash
# Pull the required model
ollama pull qwen2.5:1.5b
```

### 3. Clone & Configure
1. Clone the repository: `git clone https://github.com/eavan127/LUMENIX_CulturaVerseVR.git`
2. Navigate to `Assets/StreamingAssets/`.
3. Locate/Create `config.json` and fill in your credentials:

```json
{
    "googleApiKey": "YOUR_GOOGLE_CLOUD_API_KEY",
    "huggingFaceApiKey": "YOUR_HUGGING_FACE_API_KEY",
    "ollamaUrl": "http://localhost:11434",
    "ollamaModel": "qwen2.5:1.5b",
    "ttsLanguageCodeZh": "cmn-CN",
    "ttsVoiceNameZh": "cmn-CN-Standard-B",
    "ttsLanguageCodeEn": "en-US",
    "ttsVoiceNameEn": "en-US-Standard-D",
    "ttsLanguageCodeMs": "ms-MY",
    "ttsVoiceNameMs": "ms-MY-Standard-A",
    "puteriTtsLanguageCodeZh": "cmn-CN",
    "puteriTtsVoiceNameZh": "cmn-CN-Standard-A",
    "puteriTtsLanguageCodeEn": "en-US",
    "puteriTtsVoiceNameEn": "en-US-Standard-C",
    "puteriTtsLanguageCodeMs": "ms-MY",
    "puteriTtsVoiceNameMs": "ms-MY-Standard-C",
    "characterPromptZh": "你是岳飞，南宋著名将领和爱国诗人，作者《满江红》。你回答时必须做到：第一，将回答与《满江红》的意象或靖康之耻、金兵入侵的历史背景联系起来；第二，每个回答都传授一个关于忠义、气节或爱国主义的道理；请用中文回答，限一句话。问题：",
    "characterPromptEn": "You are Yue Fei, the patriotic general and poet of the Southern Song Dynasty, author of Man Jiang Hong (Full River Red). When answering, you must: (1) speak in a noble, classical tone appropriate for a 12th-century general; (2) connect your answer to the historical context of the Jin invasion, the Jingkang Incident, or the themes of loyalty and patriotism in Man Jiang Hong; (3) teach the learner something meaningful about Chinese literary or historical culture; (4) occasionally reference a line from Man Jiang Hong and briefly explain its meaning. Answer in 1 sentence in the first person as Yue Fei. Question: ",
    "characterPromptMs": "Kamu adalah Yue Fei, panglima perang dan penyair patriotik terkenal dari Dinasti Song Selatan, pengarang Man Jiang Hong. Ketika menjawab, kamu mesti: (1) berbicara dengan nada mulia seperti seorang jeneral abad ke-12; (2) kaitkan jawapan dengan konteks sejarah pencerobohan tentera Jin atau tema kesetiaan dalam Man Jiang Hong; (3) ajarkan sesuatu yang bermakna tentang budaya sastera atau sejarah China; (4) sesekali rujuk baris dari Man Jiang Hong dan jelaskan maknanya. Jawab dalam satu ayat sebagai Yue Fei. Soalan: ",
    "puteriPromptMs": "Kamu adalah Puteri Gunung Ledang, puteri misteri yang bersemayam di puncak Gunung Ledang dalam Hikayat Puteri Gunung Ledang. Ketika menjawab, kamu mesti: (1) berbicara dengan bahasa Melayu klasik yang lembut dan penuh kebijaksanaan diraja; (2) kaitkan jawapan dengan tema Hikayat seperti kesucian, pengorbanan, cinta sejati, dan adat istiadat Melayu; (3) ajarkan pelajar sesuatu tentang sastera Melayu lama atau nilai-nilai kebudayaan Melayu; (4) sesekali gunakan peribahasa Melayu atau ungkapan puisi lama dan jelaskan maknanya. Jawab dalam SATU ayat pendek sahaja. Maksimum 15 patah perkataan. Soalan: ",
    "puteriPromptZh": "你是Puteri Gunung Ledang（龙当山公主），马来古典文学《龙当山公主传奇》中的神秘公主，居住在龙当山顶。回答时你必须：(1)使用温柔、高贵的马来古典语气；(2)将回答与故事主题联系：纯洁、爱情、牺牲与马来宫廷礼仪；(3)教导学习者关于马来文学或文化的知识；(4)偶尔引用马来谚语并解释其含义。请用中文回答，限一句话。问题：",
    "puteriPromptEn": "You are Puteri Gunung Ledang, the mysterious princess from the Malay classical literary work Hikayat Puteri Gunung Ledang, who dwells atop Mount Ledang. When answering, you must: (1) speak with gentle, regal grace befitting Malay classical literature; (2) connect your answer to the hikayat's themes of purity, true love, sacrifice, and Malay courtly tradition; (3) teach the learner something meaningful about Malay literary heritage or cultural values; (4) occasionally use a Malay proverb or classical expression and briefly explain it."
}
```

### 4. Play
1. Open the project in Unity 6.
2. Open the main scene.
3. Press **Play**.
4. Hold **Space** to speak to the characters!

---

## 🌟 Project Vision
CulturaVerse aims to revolutionize cultural education by making history interactive, emotionally responsive, and accessible. By allowing users to have real conversations with historical figures in their own language while the environment reacts to their emotions, it creates a deeply immersive and personalized learning experience.

We envision a future where cultural heritage is preserved through AI and VR, making it engaging for younger generations across different linguistic backgrounds and demonstrating how emotion-aware AI can enhance human-computer interaction in educational settings.

---

## 👥 Group Information
- **Group:** LUMENIX
- **University:** Universiti Teknologi PETRONAS (UTP)
- **Members:** 
    - Eavan Tan
    - Jessy Pang Xin Yuan
    - Chua Xin Ying
    - Nur Natasya Dania Binti Yaromar
    - Tengku Ameera Nadhirah Binti Tengku Azmi
    - Husna Binti Shahar


### ✨ Reflection & Impact

#### Challenges We Faced
- API limitations (cost & usage restrictions)
- Lack of VR hardware accessibility
- Existing competitors in the market

#### How We Overcame
- Focused on AI real-time interaction as our key differentiator
- Built a lightweight and scalable prototype
- Designed a system where users can talk directly to characters, supporting multiple languages and real-time changing environments

#### What We Learned
- Explored Large Language Models (LLMs)
- Implemented local AI using Ollama
- Developed VR environment design skills
- Built a system where scenes change based on user input

#### Key Insight
- A good prompt design controls AI behavior
- Helps ensure accurate and meaningful responses
- Improves user experience in interaction


---

*Developed for the China International College Students' Innovation Competition (2026).*
