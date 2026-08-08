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

    // 진행자 권한은 URL의 키 하나뿐이라, 탭을 닫으면 방을 조작할 사람이 사라진다.
    // 그 사고를 막으려고 브라우저에 방별로 키를 남겨둔다.
    const hostKeyName = (code) => 'draftpick.host.' + code;

    return {
        alertTurn(message) {
            beep();
            startFlash(message);
        },
        clearAlert() {
            stopFlash();
        },
        rememberHost(code, key) {
            try {
                localStorage.setItem(hostKeyName(code), key);
            } catch {
                // 시크릿 모드나 저장 공간 차단. 링크를 직접 보관하는 수밖에 없다.
            }
        },
        recallHost(code) {
            try {
                return localStorage.getItem(hostKeyName(code));
            } catch {
                return null;
            }
        },
        forgetHost(code) {
            try {
                localStorage.removeItem(hostKeyName(code));
            } catch {
                // 지울 수 없어도 키가 틀리면 어차피 진행자로 인정되지 않는다.
            }
        },

        // 닉네임은 방마다 다시 정하기 번거로우니 브라우저 단위로 기억한다.
        rememberName(name) {
            try {
                localStorage.setItem('draftpick.name', name);
            } catch {
                // 못 저장하면 방마다 다시 입력하면 된다.
            }
        },
        recallName() {
            try {
                return localStorage.getItem('draftpick.name');
            } catch {
                return null;
            }
        },
    };
})();
