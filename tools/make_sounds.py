#!/usr/bin/env python3
"""Synthesises AVACastlesOfDrCreep's sound effects and tunes into ../src/AVACastlesOfDrCreep/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle and noise voices, and the
tunes are new compositions. The spoken welcome is made with espeak-ng (if it's installed) and then given a castle's
echo here; without espeak-ng the welcome is left as it is (or, the first time, an organ chord stands in). Run it
from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import shutil
import struct
import subprocess
import tempfile
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVACastlesOfDrCreep", "Sounds")
RNG = random.Random(1984)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def drums(bpm, bars):
    """A marching snare: a roll on the first beat of each bar, single taps on the rest, and a bass drum under it."""
    beat = 60.0 / bpm
    out = []
    for _ in range(bars):
        for b in range(4):
            if b == 0:
                hit = cat(*[tone(beat / 4, 1800, 900, "noise", attack=0.001, release=1.5, gain=0.5) for _ in range(4)])
            else:
                hit = cat(tone(beat / 2, 2000, 700, "noise", attack=0.001, release=2.5, gain=0.6), silence(beat / 2))
            kick = cat(tone(beat / 2, 110, 45, "sine", attack=0.002, release=1.5, gain=0.9), silence(beat / 2)) if b % 2 == 0 else silence(beat)
            out.extend(mix(hit, kick)[: int(RATE * beat)])
    return out



WELCOME = "Welcome, visitor...... Come in...... You will never leave!"


def read_wav(path):
    """A WAV file's samples as floats at RATE (16-bit mono, resampled simply if it's at another rate)."""
    with wave.open(path, "rb") as w:
        rate, n, width, chans = w.getframerate(), w.getnframes(), w.getsampwidth(), w.getnchannels()
        raw = w.readframes(n)
    assert width == 2
    vals = struct.unpack("<%dh" % (len(raw) // 2), raw)[::chans]
    vals = [v / 32768 for v in vals]
    if rate == RATE:
        return vals
    step = rate / RATE
    return [vals[min(len(vals) - 1, int(i * step))] for i in range(int(len(vals) / step))]


def spoken():
    """The welcome, spoken slow and deep by espeak-ng, with a stone hall's echo and a faint organ under it; or None."""
    if not shutil.which("espeak-ng"):
        return None
    with tempfile.TemporaryDirectory() as d:
        path = os.path.join(d, "say.wav")
        subprocess.run(["espeak-ng", "-v", "en+m3", "-s", "105", "-p", "18", "-a", "180", "-g", "6", "-w", path, WELCOME], check=True)
        dry = read_wav(path)
    # The castle's echo: a few delayed, quieter copies.
    out = dry + [0.0] * int(RATE * 1.2)
    for delay, gain in [(0.09, 0.45), (0.21, 0.3), (0.37, 0.2), (0.6, 0.12)]:
        k = int(RATE * delay)
        for i, v in enumerate(dry):
            out[i + k] += v * gain
    organ = mix(*[tone(len(out) / RATE, hz(n), kind="tri", attack=0.8, release=0.4, vibrato=0.004, vib_rate=5, gain=0.06) for n in ["D2", "A2", "F3"]])
    return mix(out, organ)


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav") and f != "Welcome.wav":
            os.remove(os.path.join(OUT, f))
    # Doors: a heavy creak and a thud. A bell: a bright ring. A key: a jingle. A lock: a clunk.
    write("Door.wav", cat(tone(0.35, 180, 260, "saw", vibrato=0.06, vib_rate=30, release=0.6, gain=0.5), mix(tone(0.15, 90, 50, "sine", release=1.2), tone(0.1, 400, 120, "noise", release=1.5, gain=0.4))), 0.36)
    write("Bell.wav", mix(tone(0.6, hz("E6"), kind="sine", release=0.8), tone(0.6, hz("B6") * 1.01, kind="sine", release=1.2, gain=0.4)), 0.34)
    write("Key.wav", cat(*[tone(0.05, hz(n), kind="tri", release=0.4) for n in ["E6", "G6", "C7", "G6", "C7"]]), 0.28)
    write("Unlock.wav", cat(tone(0.06, 900, 600, "square", release=1, gain=0.5), silence(0.04), mix(tone(0.12, 160, 90, "square", release=1, gain=0.6), tone(0.08, 1200, 400, "noise", release=1.4, gain=0.4))), 0.34)
    write("Switch.wav", mix(tone(0.05, 1400, 800, "square", attack=0.001, release=1.5, gain=0.5), tone(0.04, 3000, 1000, "noise", release=2, gain=0.4)), 0.3)
    write("Trap.wav", cat(mix(tone(0.08, 300, 120, "square", release=1, gain=0.5), tone(0.06, 1200, 400, "noise", release=1.5, gain=0.5)), tone(0.3, 140, 60, "saw", release=0.6, gain=0.4)), 0.34)
    write("Walkway.wav", cat(*[mix(tone(0.06, 260, 220, "square", release=1, gain=0.4), tone(0.04, 1600, 800, "noise", release=1.5, gain=0.3)) for _ in range(4)]), 0.26)
    write("FieldOff.wav", tone(0.6, 1600, 120, "saw", vibrato=0.05, vib_rate=40, release=0.5, gain=0.5), 0.26)
    write("Zap.wav", mix(tone(0.4, 3000, 600, "noise", attack=0.001, release=0.6), tone(0.35, 80, 60, "saw", vibrato=0.3, vib_rate=60, release=0.6, gain=0.5)), 0.44)
    write("Ray.wav", mix(tone(0.35, 1800, 300, "square", vibrato=0.15, vib_rate=45, release=0.6, gain=0.6), tone(0.3, 4000, 1500, "noise", release=0.8, gain=0.3)), 0.32)
    write("Teleport.wav", cat(*[tone(0.04, 400 + i * 180, kind="sine", release=0.3) for i in range(10)], tone(0.25, 2200, 600, "sine", vibrato=0.04, vib_rate=25, release=0.6)), 0.3)
    # The mummy's groan, and the monster's roar.
    write("MummyRise.wav", cat(tone(0.25, 300, 200, "noise", attack=0.05, release=0.6, gain=0.4), tone(1.1, 110, 85, "saw", attack=0.2, vibrato=0.06, vib_rate=4, release=0.5)), 0.42)
    write("MonsterWake.wav", mix(tone(1.0, 75, 55, "saw", attack=0.05, vibrato=0.12, vib_rate=9, release=0.4), tone(0.9, 400, 200, "noise", attack=0.05, release=0.5, gain=0.4)), 0.46)
    write("Fall.wav", tone(0.5, 1200, 300, "sine", vibrato=0.01, release=0.4), 0.22)
    write("Die.wav", cat(tone(0.12, 500, 200, "square", release=0.6), *[tone(0.16, hz(n), kind="tri", release=0.5) for n in ["D5", "A4", "F4", "D4"]]), 0.42)
    write("Escape.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.3) for n in ["D5", "F5", "A5", "D6"]], tone(0.3, hz("D6"), kind="pulse", release=0.5)), 0.36)
    write("LevelStart.wav", mix(*[tone(1.6, hz(n), kind="tri", attack=0.05, release=0.6, vibrato=0.004, vib_rate=5) for n in ["D3", "A3", "D4", "F4"]]), 0.34)
    write("LevelClear.wav", tune(140, [("D5", 0.5), ("F5", 0.5), ("A5", 0.5), ("D6", 1), ("C#6", 0.5), ("D6", 2)], [("D3", 1), ("A2", 1), ("D3", 1), ("A2", 0.5), ("D3", 1.5)], kind="pulse"), 0.4)

    # In the castle: a slow, creeping organ-ish line in D minor (an original composition), a whole number of beats so it loops.
    lead = [("D4", 1), ("F4", 1), ("A4", 1), ("G#4", 1), ("A4", 2), ("F4", 1), ("E4", 1),
            ("D4", 1), ("A3", 1), ("C4", 1), ("Bb3", 1), ("A3", 4)]
    bass = [("D2", 2), ("D2", 2), ("F2", 2), ("A2", 2), ("Bb2", 2), ("G2", 2), ("A2", 4)]
    melody = tune(84, lead, bass, kind="tri")
    write("Music.wav", melody, 0.26)

    # The title music: the same, fuller, with a second strain.
    title_lead = lead + [("F4", 1), ("A4", 1), ("D5", 1), ("C#5", 1), ("D5", 2), ("A4", 1), ("Bb4", 1),
                         ("G4", 1), ("E4", 1), ("F4", 1), ("E4", 1), ("D4", 4)]
    title_bass = bass + [("D2", 2), ("F2", 2), ("Bb2", 2), ("G2", 2), ("C3", 2), ("A2", 2), ("D2", 4)]
    write("TitleTune.wav", cat(tune(84, title_lead, title_bass, kind="tri"), silence(1.0)), 0.4)
    write("StartTune.wav", tune(120, [("A4", 0.5), ("D5", 0.5), ("F5", 0.5), ("A5", 1), ("G#5", 0.5), ("A5", 1)], [("D3", 1), ("A2", 1), ("D3", 2)], kind="tri"), 0.42)
    write("GameOver.wav", tune(60, [("A4", 1), ("F4", 1), ("E4", 1), ("C#4", 1), ("D4", 4)], [("D3", 2), ("Bb2", 2), ("A2", 2), ("D2", 2)], kind="tri"), 0.42)

    # The welcome.
    said = spoken()
    if said is not None:
        write("Welcome.wav", said, 0.8)
        print("welcome spoken by espeak-ng")
    elif not os.path.exists(os.path.join(OUT, "Welcome.wav")):
        write("Welcome.wav", mix(*[tone(3.5, hz(n), kind="tri", attack=0.4, release=0.5, vibrato=0.006, vib_rate=5) for n in ["D3", "F3", "A3", "D4"]]), 0.4)
        print("espeak-ng isn't installed: an organ chord stands in for the welcome")


if __name__ == "__main__":
    main()
