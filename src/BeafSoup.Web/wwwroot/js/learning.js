window.beafAudio = (() => {
  let current = null;
  let generation = 0;
  let settlePending = null;

  const release = (outcome) => {
    if (settlePending) {
      const settle = settlePending;
      settlePending = null;
      settle(outcome);
    }
  };

  const stopCurrent = () => {
    if (!current) return;
    current.onended = null;
    current.onerror = null;
    current.pause();
    current.currentTime = 0;
    current = null;
  };

  const start = (url) => {
    generation++;
    stopCurrent();
    release("stopped");
    const audio = new Audio(url);
    current = audio;
    audio.preload = "auto";
    return audio;
  };

  return {
    // Starts a clip and resolves once playback has begun. Used by one-shot buttons.
    play: async (url) => {
      const audio = start(url);
      try {
        await audio.play();
      } catch (error) {
        // A newer clip replaced this one; that abort is expected, not a failure.
        if (error.name === "AbortError") return;
        throw error;
      }
    },
    // Plays a clip to the end: resolves "ended", "error" or "stopped".
    playToEnd: (url) => new Promise((resolve) => {
      const audio = start(url);
      const token = generation;
      settlePending = resolve;
      audio.onended = () => {
        if (audio === current) current = null;
        release("ended");
      };
      audio.onerror = () => {
        if (audio === current) current = null;
        release("error");
      };
      audio.play().catch(() => {
        if (token === generation) release("error");
      });
    }),
    stop: () => {
      generation++;
      stopCurrent();
      release("stopped");
    }
  };
})();

window.beafStorage = {
  get: (key) => window.localStorage.getItem(key),
  set: (key, value) => window.localStorage.setItem(key, value)
};
