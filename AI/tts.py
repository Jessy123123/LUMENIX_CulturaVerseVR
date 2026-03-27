import edge_tts
import asyncio
import sys

text = sys.argv[1]
output_file = "yuefei_voice.mp3"

async def main():
    communicate = edge_tts.Communicate(text, "en-US-JennyNeural")
    await communicate.save(output_file)

asyncio.run(main())

print("Saved:", output_file)