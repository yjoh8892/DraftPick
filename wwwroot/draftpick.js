// 내 차례가 왔을 때 알리는 최소한의 장치.
// 디스코드를 보다가 탭을 놓치는 상황이 흔해서, 소리와 탭 제목 두 가지로 알린다.
window.draftPick = (() => {
    let flashTimer = null;
    let titleBeforeFlash = null;
    let audio = null;

    // 오디오 파일 없이 짧은 두 음을 만들어 낸다.
    function beep() {
        try {
            audio ??= new (window.AudioContext || window.webkitAudioContext)();
            if (audio.state === 'suspended') audio.resume();

            const start = audio.currentTime;
            [880, 1320].forEach((frequency, i) => {
                const at = start + i * 0.18;
                const osc = audio.createOscillator();
                const gain = audio.createGain();

                osc.type = 'sine';
                osc.frequency.value = frequency;
                gain.gain.setValueAtTime(0.0001, at);
                gain.gain.exponentialRampToValueAtTime(0.25, at + 0.02);
                gain.gain.exponentialRampToValueAtTime(0.0001, at + 0.16);

                osc.connect(gain).connect(audio.destination);
                osc.start(at);
                osc.stop(at + 0.18);
            });
        } catch {
            // 브라우저가 소리를 막아도 제목 깜빡임은 그대로 동작한다.
        }
    }

    function startFlash(message) {
        stopFlash();
        titleBeforeFlash = document.title;

        let showingMessage = false;
        flashTimer = setInterval(() => {
            showingMessage = !showingMessage;
            document.title = showingMessage ? message : titleBeforeFlash;
        }, 800);
    }

    function stopFlash() {
        if (flashTimer === null) return;

        clearInterval(flashTimer);
        flashTimer = null;
        if (titleBeforeFlash !== null) document.title = titleBeforeFlash;
        titleBeforeFlash = null;
    }

    return {
        alertTurn(message) {
            beep();
            startFlash(message);
        },
        clearAlert() {
            stopFlash();
        },
    };
})();
