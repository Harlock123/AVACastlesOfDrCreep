#!/usr/bin/env python3
"""Builds the castle files in ../src/AVACastlesOfDrCreep/Castles from the designs below.

Each room is laid out by placing floors, ladders, poles, trapdoors, walkways and things at squares (24 across,
16 down; a thing stands on the floor row given). Each castle ends with its way out, step by step: the demonstration
walks it, and the tests prove every castle can be escaped by it.
    python3 tools/make_castles.py
"""
import os

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVACastlesOfDrCreep", "Castles")
W, H = 24, 16


class Room:
    def __init__(self, rid, name, mx, my, mw=3, mh=2):
        self.id, self.name, self.map = rid, name, (mx, my, mw, mh)
        self.g = [[" "] * W for _ in range(H)]
        self.lines = []

    def _set(self, x, y, ch, over=" ="):
        assert 0 <= x < W and 0 <= y < H, (self.name, x, y)
        assert self.g[y][x] in over, f"{self.name}: ({x},{y}) is already '{self.g[y][x]}'"
        self.g[y][x] = ch

    def floor(self, row, x0, x1):
        for x in range(x0, x1 + 1):
            if self.g[row][x] == " ":
                self.g[row][x] = "="
        return self

    def ladder(self, col, top, bottom):
        """From the floor at row top down to the floor at row bottom."""
        self._set(col, top, "#")
        for r in range(top + 1, bottom):
            self._set(col, r, "H")
        assert self.g[bottom][col] in "=#!", f"{self.name}: ladder at {col} has no floor at {bottom}"
        return self

    def pole(self, col, top, bottom):
        self._set(col, top, "!")
        for r in range(top + 1, bottom):
            self._set(col, r, "|")
        assert self.g[bottom][col] in "=#!", f"{self.name}: pole at {col} has no floor at {bottom}"
        return self

    def trap(self, row, x0, x1):
        for x in range(x0, x1 + 1):
            self._set(x, row, "T")
        return self

    def walkway(self, row, x0, x1, way):
        for x in range(x0, x1 + 1):
            self._set(x, row, {"left": "<", "right": ">", "still": "-"}[way])
        return self

    def thing(self, letter, col, floor_row, kind, *args):
        """A thing standing on the floor at floor_row (it sits in the square above it)."""
        assert self.g[floor_row][col] not in " H|", f"{self.name}: {letter} at {col} has nothing to stand on at {floor_row}"
        self._set(col, floor_row - 1, letter)
        self.lines.append(" ".join([letter, kind, *map(str, args)]))
        return self

    def text(self):
        mx, my, mw, mh = self.map
        rows = ["".join("." if c == " " else c for c in row) for row in self.g]
        return "\n".join([f"room {self.id} {mx} {my} {mw} {mh} {self.name}", *rows, *self.lines, ""])


def castle(name, par, start, rooms, solution):
    return "\n".join([f"castle {name}", f"par {par}", f"start {start[0]} {start[1]}", "", *[r.text() for r in rooms], "solution", *solution, ""])


# ─────────────────────────────── the tutorial ───────────────────────────────

def tutorial():
    r0 = Room(0, "The Gatehouse", 0, 0)
    r0.floor(15, 0, 23).floor(10, 5, 20).ladder(8, 10, 15)
    r0.thing("s", 2, 15, "spawn").thing("b", 12, 10, "bell", "a").thing("a", 17, 10, "door", 1, "a")

    r1 = Room(1, "The Key Hall", 4, 0)
    r1.floor(15, 0, 23).floor(10, 1, 10).floor(10, 14, 23).pole(9, 10, 15).ladder(22, 10, 15)
    r1.thing("a", 3, 10, "door", 0, "a").thing("k", 18, 15, "key", "red").thing("l", 19, 10, "lock", "red", "d").thing("d", 16, 10, "door", 2, "a")

    r2 = Room(2, "The Trapdoor", 8, 0)
    r2.floor(15, 0, 23).trap(15, 5, 8)
    r2.thing("a", 1, 15, "door", 1, "d").thing("t", 3, 15, "trapswitch", 1).thing("c", 14, 15, "coffin", "left").thing("d", 21, 15, "door", 3, "a", "open")

    r3 = Room(3, "The Machines", 12, 0)
    r3.floor(15, 0, 23)
    r3.thing("a", 2, 15, "door", 2, "d").thing("w", 6, 15, "boltswitch", "z").thing("z", 10, 15, "bolt", 3)
    r3.thing("g", 14, 15, "fieldbutton", "f").thing("f", 17, 15, "field", 3).thing("d", 21, 15, "door", 4, "a", "open")

    r4 = Room(4, "The Way Out", 16, 0)
    r4.floor(15, 0, 23).floor(10, 9, 15).floor(5, 14, 23)
    r4.thing("a", 2, 15, "door", 3, "d").thing("m", 5, 15, "booth").thing("r", 18, 5, "receiver", "m", "red").thing("q", 12, 10, "receiver", "m", "blue")
    r4.thing("e", 21, 5, "exit", "open")

    return castle("Tutorial", 60, (0, "s"), [r0, r1, r2, r3, r4], [
        "walk 8", "up", "walk 12", "fire", "walk 17", "door",
        "walk 9", "down", "walk 18", "walk 22", "up", "walk 19", "fire", "walk 16", "door",
        "walk 8", "walk 3", "fire", "wait 3", "fire", "walk 21", "door",
        "walk 6", "fire", "walk 14", "fire", "walk 21", "door",
        "walk 5", "door", "walk 21", "door",
    ])


# ─────────────────────────────── Ravenmoor ───────────────────────────────

def ravenmoor():
    r0 = Room(0, "The Great Hall", 0, 2)
    r0.floor(15, 0, 23).floor(10, 13, 23).ladder(20, 10, 15)
    r0.thing("u", 2, 15, "tomb").thing("s", 6, 15, "spawn").thing("n", 9, 15, "ankh", "u").thing("a", 15, 10, "door", 1, "a", "open")

    r1 = Room(1, "The Gallery", 4, 2)
    r1.floor(10, 0, 23).walkway(10, 8, 18, "right").floor(15, 0, 23)
    r1.thing("a", 2, 10, "door", 0, "a").thing("x", 4, 10, "raybox", "y").thing("y", 23, 10, "ray", 1, "left").thing("d", 21, 10, "door", 2, "a", "open")

    r2 = Room(2, "The Crypt", 8, 1, 3, 3)
    r2.floor(15, 0, 23).floor(10, 0, 9).floor(10, 14, 23).pole(16, 10, 15).ladder(4, 10, 15)
    r2.thing("a", 21, 10, "door", 1, "d").thing("c", 12, 15, "coffin", "left").thing("z", 14, 15, "bolt", 3, "off").thing("w", 18, 15, "boltswitch", "z")
    r2.thing("k", 2, 15, "key", "green").thing("e", 7, 10, "door", 3, "a", "open")

    r3 = Room(3, "The Tower", 12, 1, 3, 3)
    r3.floor(15, 0, 23).floor(10, 3, 16).floor(5, 10, 23).ladder(6, 10, 15).ladder(13, 5, 10)
    r3.thing("a", 2, 15, "door", 2, "e", "open").thing("g", 12, 5, "fieldbutton", "f").thing("f", 16, 5, "field", 3)
    r3.thing("l", 18, 5, "lock", "green", "e").thing("e", 21, 5, "exit")

    return castle("Ravenmoor", 90, (0, "s"), [r0, r1, r2, r3], [
        "walk 20", "up", "walk 15", "door",
        "walk 4", "aim y 1", "walk 21", "door",
        "walk 16", "down", "walk 18", "fire", "wait 2.5", "fire", "walk 2", "walk 4", "up", "walk 7", "door",
        "walk 6", "up", "walk 13", "up", "walk 12", "fire", "walk 18", "fire", "walk 21", "door",
    ])


# ─────────────────────────────── Grimwald ───────────────────────────────

def grimwald():
    r0 = Room(0, "The Drawbridge", 0, 1)
    r0.floor(15, 0, 23).walkway(15, 6, 17, "left").floor(10, 12, 23).ladder(20, 10, 15)
    r0.thing("s", 1, 15, "spawn").thing("c", 4, 15, "walkcontrol", 1).thing("a", 14, 10, "door", 1, "a", "open")

    r1 = Room(1, "The Mummy's Hall", 4, 1)
    r1.floor(15, 0, 23).trap(15, 11, 13)
    r1.thing("a", 2, 15, "door", 0, "a").thing("t", 6, 15, "trapswitch", 1).thing("n", 8, 15, "ankh", "u").thing("u", 18, 15, "tomb")
    r1.thing("d", 21, 15, "door", 2, "a", "open")

    r2 = Room(2, "The Lightning Gallery", 8, 1)
    r2.floor(15, 0, 23).floor(10, 0, 12).ladder(12, 10, 15).pole(5, 10, 15)
    r2.thing("w", 2, 10, "boltswitch", "z").thing("k", 3, 15, "key", "blue").thing("z", 8, 15, "bolt", 3)
    r2.thing("d", 15, 15, "door", 3, "a", "open").thing("a", 21, 15, "door", 1, "d")

    r3 = Room(3, "The Transmitter", 12, 1)
    r3.floor(15, 0, 23).floor(10, 9, 14).floor(5, 16, 23)
    r3.thing("a", 2, 15, "door", 2, "d").thing("m", 6, 15, "booth").thing("q", 12, 10, "receiver", "m", "green")
    r3.thing("c", 17, 5, "coffin", "right").thing("f", 19, 5, "field", 3).thing("r", 20, 5, "receiver", "m", "red")
    r3.thing("d", 21, 5, "door", 4, "a").thing("b", 23, 5, "bell", "d")

    r4 = Room(4, "The Gun Room", 12, 3)
    r4.floor(10, 0, 23).floor(15, 0, 23)
    r4.thing("y", 0, 10, "ray", 2, "right").thing("a", 2, 10, "door", 3, "d").thing("x", 4, 10, "raybox", "y").thing("c", 10, 10, "coffin", "left")
    r4.thing("d", 21, 10, "door", 5, "a", "open")

    r5 = Room(5, "The Keep", 16, 2, 3, 3)
    r5.floor(15, 0, 23).floor(10, 2, 17).floor(5, 12, 23).ladder(5, 10, 15).ladder(13, 5, 10)
    r5.thing("a", 2, 15, "door", 4, "d").thing("n", 9, 10, "ankh", "u").thing("u", 17, 10, "tomb")
    r5.thing("l", 19, 5, "lock", "blue", "e").thing("e", 21, 5, "exit")

    return castle("Grimwald", 120, (0, "s"), [r0, r1, r2, r3, r4, r5], [
        "walk 4", "fire", "walk 20", "up", "walk 14", "door",
        "walk 8", "walk 6", "wait 2.8", "fire", "wait 1", "fire", "walk 21", "door",
        "walk 12", "up", "walk 2", "fire", "walk 5", "down", "walk 3", "walk 15", "door",
        "walk 6", "door", "walk 23", "fire", "walk 21", "door",
        "walk 4", "wait 1.2", "fire", "aim y 2", "walk 21", "door",
        "walk 5", "up", "walk 13", "up", "walk 19", "fire", "walk 21", "door",
    ])


# ─────────────────────────────── Mortlake ───────────────────────────────

def mortlake():
    r0 = Room(0, "The Entrance Hall", 6, 2, 3, 3)
    r0.floor(15, 0, 23).floor(10, 0, 10).floor(5, 9, 23).ladder(8, 10, 15).ladder(10, 5, 10)
    r0.thing("s", 3, 15, "spawn").thing("a", 2, 10, "door", 2, "a", "open")
    r0.thing("l", 18, 15, "lock", "yellow", "b").thing("b", 21, 15, "door", 3, "a")
    r0.thing("m", 17, 5, "lock", "white", "c").thing("c", 20, 5, "door", 4, "a")

    r2 = Room(2, "The Bell Tower", 2, 0)
    r2.floor(15, 0, 23).floor(10, 0, 19).floor(5, 16, 23).ladder(18, 5, 10).ladder(5, 10, 15).pole(22, 5, 15)
    r2.thing("a", 2, 10, "door", 0, "a").thing("b", 20, 5, "bell", "d").thing("d", 20, 15, "door", 1, "a")

    r1 = Room(1, "The Conveyor", 6, 0)
    r1.floor(10, 12, 23).floor(15, 0, 23).walkway(15, 4, 11, "right").pole(13, 10, 15).ladder(20, 10, 15)
    r1.thing("a", 21, 10, "door", 2, "d", "open").thing("v", 17, 10, "boltswitch", "z").thing("z", 9, 15, "bolt", 3)
    r1.thing("w", 15, 15, "walkcontrol", 1).thing("k", 2, 15, "key", "yellow")

    r3 = Room(3, "The Mortuary", 10, 2)
    r3.floor(15, 0, 23).trap(15, 10, 12)
    r3.thing("a", 2, 15, "door", 0, "b").thing("t", 7, 15, "trapswitch", 1).thing("n", 14, 15, "ankh", "u").thing("u", 20, 15, "tomb")
    r3.thing("k", 22, 15, "key", "white")

    r4 = Room(4, "The Generator", 10, 0, 3, 3)
    r4.floor(5, 0, 12).floor(10, 8, 23).floor(15, 0, 23).pole(11, 5, 10).ladder(21, 10, 15)
    r4.thing("a", 3, 5, "door", 0, "c").thing("x", 9, 5, "raybox", "y").thing("y", 23, 10, "ray", 2, "left")
    r4.thing("e", 1, 15, "exit", "open").thing("c", 4, 15, "coffin", "right").thing("z", 6, 15, "bolt", 3).thing("f", 9, 15, "field", 3)
    r4.thing("g", 10, 15, "fieldbutton", "f").thing("w", 15, 15, "boltswitch", "z")

    return castle("Mortlake", 180, (0, "s"), [r0, r2, r1, r3, r4], [
        "walk 8", "up", "walk 2", "door",
        "walk 18", "up", "walk 20", "fire", "walk 22", "down", "walk 20", "door",
        "walk 17", "fire", "walk 13", "down", "walk 15", "fire", "walk 2", "walk 20", "up", "walk 21", "door",
        "walk 5", "up", "walk 2", "door",
        "walk 8", "down", "walk 18", "fire", "walk 21", "door",
        "walk 14", "walk 7", "wait 3.2", "fire", "wait 1", "fire", "walk 22", "walk 2", "door",
        "walk 8", "up", "walk 10", "up", "walk 17", "fire", "walk 20", "door",
        "walk 9", "aim y 2", "walk 11", "down", "walk 21", "down", "walk 10", "wait 2", "walk 15", "fire", "walk 10", "fire", "walk 1", "door",
    ])


# ─────────────────────────────── Blackspire ───────────────────────────────

def blackspire():
    r0 = Room(0, "The Cellar", 0, 3)
    r0.floor(15, 0, 23).floor(10, 0, 23).trap(10, 14, 16).ladder(4, 10, 15)
    r0.thing("s", 6, 15, "spawn").thing("c", 10, 15, "coffin", "left").thing("t", 19, 10, "trapswitch", 1).thing("d", 21, 10, "door", 1, "a", "open")

    r1 = Room(1, "The Clockwork Hall", 4, 3)
    r1.floor(15, 0, 23).walkway(15, 4, 20, "left").floor(10, 0, 10).ladder(1, 10, 15)
    r1.thing("w", 6, 10, "walkcontrol", 1).thing("n", 8, 10, "ankh", "u").thing("u", 10, 10, "tomb")
    r1.thing("a", 2, 15, "door", 0, "d").thing("g", 3, 15, "fieldbutton", "f").thing("f", 18, 15, "field", 3).thing("d", 22, 15, "door", 2, "a", "open")

    r2 = Room(2, "The Hall of Mirrors", 8, 3)
    r2.floor(15, 0, 23).floor(10, 9, 23).floor(5, 17, 23)
    r2.thing("a", 1, 15, "door", 1, "d").thing("m", 3, 15, "booth").thing("p", 19, 5, "receiver", "m", "red").thing("z", 22, 5, "bolt", 3)
    r2.thing("q", 12, 10, "receiver", "m", "green").thing("d", 21, 10, "door", 3, "a", "open")

    r3 = Room(3, "The Gun Gallery", 12, 3)
    r3.floor(10, 0, 23).walkway(10, 10, 20, "left").floor(15, 0, 23)
    r3.thing("a", 1, 10, "door", 2, "d").thing("x", 3, 10, "raybox", "y").thing("w", 8, 10, "walkcontrol", 1).thing("c", 9, 10, "coffin", "left")
    r3.thing("d", 21, 10, "door", 4, "a", "open").thing("y", 23, 10, "ray", 1, "left")

    r4 = Room(4, "The Laboratory", 16, 3)
    r4.floor(15, 0, 23).floor(10, 0, 23).ladder(4, 10, 15).ladder(8, 10, 15)
    r4.thing("a", 1, 15, "door", 3, "d").thing("z", 6, 15, "bolt", 3).thing("w", 10, 15, "boltswitch", "y").thing("y", 12, 15, "bolt", 3)
    r4.thing("v", 15, 15, "boltswitch", "x").thing("x", 17, 15, "bolt", 3).thing("k", 19, 15, "key", "red").thing("d", 21, 15, "door", 5, "a", "open")
    r4.thing("n", 12, 10, "ankh", "u").thing("u", 20, 10, "tomb")

    r5 = Room(5, "The Spire", 20, 1, 3, 4)
    r5.floor(15, 0, 23).floor(10, 2, 20).floor(5, 3, 23).trap(5, 8, 10).ladder(14, 10, 15).ladder(6, 5, 10)
    r5.thing("a", 2, 15, "door", 4, "d").thing("t", 4, 5, "trapswitch", 1).thing("c", 12, 5, "coffin", "left")
    r5.thing("l", 18, 5, "lock", "red", "e").thing("e", 21, 5, "exit")

    return castle("Blackspire", 240, (0, "s"), [r0, r1, r2, r3, r4, r5], [
        "walk 4", "up", "walk 19", "watch 15", "fire", "wait 1", "fire", "walk 21", "door",
        "walk 1", "up", "walk 6", "fire", "walk 1", "down", "walk 3", "fire", "walk 22", "door",
        "walk 3", "fire", "door", "walk 21", "door",
        "walk 3", "watch 6", "fire", "aim y 1", "walk 8", "fire", "walk 21", "door",
        "walk 4", "up", "walk 8", "down", "walk 10", "fire", "walk 15", "fire", "walk 21", "door",
        "walk 14", "up", "walk 6", "up", "walk 4", "watch 9", "fire", "wait 1", "fire", "walk 18", "fire", "walk 21", "door",
    ])


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".castle"):
            os.remove(os.path.join(OUT, f))
    for i, (slug, make) in enumerate([("tutorial", tutorial), ("ravenmoor", ravenmoor), ("grimwald", grimwald), ("mortlake", mortlake), ("blackspire", blackspire)]):
        with open(os.path.join(OUT, f"{i}-{slug}.castle"), "w") as f:
            f.write(make())
    print("written", sorted(os.listdir(OUT)))
