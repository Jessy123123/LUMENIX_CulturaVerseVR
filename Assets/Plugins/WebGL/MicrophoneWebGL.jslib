var MicrophoneWebGLPlugin = {

  // ── Internal state shared across all calls ────────────────────────────
  $micState: {
    stream:    null,
    audioCtx:  null,
    processor: null,
    chunks:    [],
    recording: false,
    goName:    ""
  },

  // ── Simple linear resampler (float32) ────────────────────────────────
  $micResample: function(input, fromRate, toRate) {
    if (fromRate === toRate) return input;
    var ratio  = fromRate / toRate;
    var length = Math.round(input.length / ratio);
    var out    = new Float32Array(length);
    for (var i = 0; i < length; i++) {
      var src   = i * ratio;
      var lo    = Math.floor(src);
      var hi    = Math.min(lo + 1, input.length - 1);
      var frac  = src - lo;
      out[i]    = input[lo] * (1 - frac) + input[hi] * frac;
    }
    return out;
  },

  // ── Start recording ───────────────────────────────────────────────────
  // goNamePtr: UTF8 pointer to the Unity GameObject name that will receive
  //            the SendMessage callbacks.
  MicWebGL_StartRecording: function(goNamePtr) {
    var goName = UTF8ToString(goNamePtr);
    micState.goName   = goName;
    micState.chunks   = [];
    micState.recording = false;

    var constraints = {
      audio: {
        sampleRate:   { ideal: 16000 },
        channelCount: { ideal: 1 },
        echoCancellation: true,
        noiseSuppression: true
      },
      video: false
    };

    navigator.mediaDevices.getUserMedia(constraints)
      .then(function(stream) {
        micState.stream = stream;

        // Request 16 kHz context; browsers may not honour it exactly
        var CtxClass = window.AudioContext || window.webkitAudioContext;
        micState.audioCtx = new CtxClass({ sampleRate: 16000 });

        var source    = micState.audioCtx.createMediaStreamSource(stream);
        // bufferSize 4096 — good balance of latency vs overhead
        micState.processor = micState.audioCtx.createScriptProcessor(4096, 1, 1);

        micState.processor.onaudioprocess = function(e) {
          if (!micState.recording) return;
          // Copy channel data — it's reused after the event
          var ch = e.inputBuffer.getChannelData(0);
          micState.chunks.push(new Float32Array(ch));
        };

        source.connect(micState.processor);
        micState.processor.connect(micState.audioCtx.destination);
        micState.recording = true;

        SendMessage(micState.goName, 'OnWebGLMicStarted', 'ok');
      })
      .catch(function(err) {
        console.error('MicWebGL: getUserMedia failed:', err);
        SendMessage(micState.goName, 'OnWebGLMicError', err.message || String(err));
      });
  },

  // ── Stop recording and deliver base64 PCM16 to Unity ─────────────────
  MicWebGL_StopRecording: function() {
    if (!micState.recording) return;
    micState.recording = false;

    // Stop all media tracks
    if (micState.stream) {
      micState.stream.getTracks().forEach(function(t) { t.stop(); });
      micState.stream = null;
    }

    if (micState.processor) {
      micState.processor.disconnect();
      micState.processor = null;
    }

    // Merge chunk arrays into one Float32Array
    var totalLen = 0;
    micState.chunks.forEach(function(c) { totalLen += c.length; });

    var merged = new Float32Array(totalLen);
    var offset = 0;
    micState.chunks.forEach(function(c) { merged.set(c, offset); offset += c.length; });
    micState.chunks = [];

    // Resample to 16 kHz if the AudioContext ran at a different rate
    var ctxRate = micState.audioCtx ? micState.audioCtx.sampleRate : 16000;
    var resampled = micResample(merged, ctxRate, 16000);

    // Close AudioContext
    if (micState.audioCtx) {
      micState.audioCtx.close().catch(function(){});
      micState.audioCtx = null;
    }

    // Convert float32 → int16 PCM
    var pcm16 = new Int16Array(resampled.length);
    for (var i = 0; i < resampled.length; i++) {
      var s   = Math.max(-1, Math.min(1, resampled[i]));
      pcm16[i] = s < 0 ? (s * 0x8000) : (s * 0x7FFF);
    }

    // Encode as base64 (raw PCM16, no WAV header — matches what Google STT wants)
    var bytes  = new Uint8Array(pcm16.buffer);
    var binary = '';
    // Build string in 8 KB chunks to avoid call-stack overflow on large buffers
    var CHUNK = 8192;
    for (var i = 0; i < bytes.length; i += CHUNK) {
      binary += String.fromCharCode.apply(null, bytes.subarray(i, i + CHUNK));
    }
    var b64 = btoa(binary);

    SendMessage(micState.goName, 'OnWebGLAudioReady', b64);
  },

  // ── Query recording state (optional utility) ──────────────────────────
  MicWebGL_IsRecording: function() {
    return micState.recording ? 1 : 0;
  }
};

autoAddDeps(MicrophoneWebGLPlugin, '$micState');
autoAddDeps(MicrophoneWebGLPlugin, '$micResample');
mergeInto(LibraryManager.library, MicrophoneWebGLPlugin);
