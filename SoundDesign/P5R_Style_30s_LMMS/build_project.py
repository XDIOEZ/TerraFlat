from __future__ import annotations

from pathlib import Path
import xml.etree.ElementTree as ET


BPM = 134
TICKS_PER_BAR = 192
BARS = 16
SONG_LEN = TICKS_PER_BAR * BARS


ROOT = Path(__file__).resolve().parent
PROJECT_PATH = ROOT / "P5R_Style_30s.mmp"


def add_common_instrument_nodes(
    instrument_track: ET.Element,
    *,
    filter_cutoff: int,
    filter_resonance: float,
    envelope: bool,
    attack: float = 0.0,
    decay: float = 0.25,
    sustain: float = 0.5,
    release: float = 0.12,
) -> None:
    eldata = ET.SubElement(
        instrument_track,
        "eldata",
        {
            "fwet": "1" if filter_cutoff < 14000 else "0",
            "ftype": "7" if filter_cutoff < 14000 else "0",
            "fres": str(filter_resonance),
            "fcut": str(filter_cutoff),
        },
    )
    ET.SubElement(
        eldata,
        "elvol",
        {
            "lspd": "0.1",
            "ctlenvamt": "0",
            "lpdel": "0",
            "pdel": "0",
            "amt": "1" if envelope else "0",
            "hold": "0",
            "syncmode": "0",
            "userwavefile": "",
            "latt": "0",
            "sustain": str(sustain),
            "lamt": "0",
            "lshp": "0",
            "lspd_denominator": "4",
            "lspd_numerator": "4",
            "x100": "0",
            "rel": str(release),
            "dec": str(decay),
            "att": str(attack),
        },
    )
    for name in ("elcut", "elres"):
        ET.SubElement(
            eldata,
            name,
            {
                "lspd": "0.1",
                "ctlenvamt": "0",
                "lpdel": "0",
                "pdel": "0",
                "amt": "0",
                "hold": "0.5",
                "syncmode": "0",
                "userwavefile": "",
                "latt": "0",
                "sustain": "0.5",
                "lamt": "0",
                "lshp": "0",
                "lspd_denominator": "4",
                "lspd_numerator": "4",
                "x100": "0",
                "rel": "0.1",
                "dec": "0.5",
                "att": "0",
            },
        )
    ET.SubElement(instrument_track, "chordcreator", {"chordrange": "1", "chord": "0", "chord-enabled": "0"})
    ET.SubElement(
        instrument_track,
        "arpeggiator",
        {
            "arpdir": "0",
            "arpgate": "100",
            "arptime_denominator": "4",
            "syncmode": "0",
            "arp-enabled": "0",
            "arprange": "1",
            "arptime_numerator": "4",
            "arpmode": "0",
            "arp": "0",
            "arptime": "100",
        },
    )
    ET.SubElement(
        instrument_track,
        "midiport",
        {
            "inputchannel": "0",
            "fixedinputvelocity": "-1",
            "outputcontroller": "0",
            "outputchannel": "1",
            "fixedoutputvelocity": "-1",
            "readable": "0",
            "fixedoutputnote": "-1",
            "outputprogram": "1",
            "writable": "0",
            "basevelocity": "127",
            "inputcontroller": "0",
        },
    )
    ET.SubElement(instrument_track, "fxchain", {"numofeffects": "0", "enabled": "0"})


def add_track(
    container: ET.Element,
    name: str,
    notes: list[tuple[int, int, int, int]],
    *,
    volume: int,
    pan: int = 0,
    sample: str | None = None,
    synth: dict[str, int | str] | None = None,
    filter_cutoff: int = 14000,
    filter_resonance: float = 0.5,
    attack: float = 0.0,
    decay: float = 0.25,
    sustain: float = 0.5,
    release: float = 0.12,
) -> None:
    track = ET.SubElement(container, "track", {"type": "0", "muted": "0", "name": name, "solo": "0"})
    it = ET.SubElement(
        track,
        "instrumenttrack",
        {
            "pitch": "0",
            "vol": str(volume),
            "fxch": "0",
            "pan": str(pan),
            "basenote": "57",
            "usemasterpitch": "1",
            "pitchrange": "1",
        },
    )
    if sample is not None:
        instrument = ET.SubElement(it, "instrument", {"name": "audiofileprocessor"})
        ET.SubElement(
            instrument,
            "audiofileprocessor",
            {
                "interp": "1",
                "reversed": "0",
                "looped": "0",
                "sframe": "0",
                "lframe": "0",
                "eframe": "1",
                "src": sample,
                "amp": "100",
                "stutter": "0",
            },
        )
        add_common_instrument_nodes(it, filter_cutoff=14000, filter_resonance=0.5, envelope=False)
    else:
        params = {
            "finel0": "0",
            "vol1": "55",
            "finel1": "-5",
            "coarse0": "0",
            "finer0": "0",
            "vol2": "30",
            "pan0": "0",
            "finer1": "5",
            "coarse1": "0",
            "finel2": "0",
            "pan1": "0",
            "coarse2": "0",
            "finer2": "0",
            "pan2": "0",
            "wavetype0": "2",
            "wavetype1": "0",
            "wavetype2": "3",
            "phoffset0": "0",
            "phoffset1": "0",
            "phoffset2": "0",
            "modalgo1": "2",
            "modalgo2": "2",
            "modalgo3": "2",
            "stphdetun0": "0",
            "stphdetun1": "0",
            "stphdetun2": "0",
            "userwavefile0": "",
            "userwavefile1": "",
            "userwavefile2": "",
            "vol0": "100",
        }
        if synth:
            params.update({k: str(v) for k, v in synth.items()})
        instrument = ET.SubElement(it, "instrument", {"name": "tripleoscillator"})
        ET.SubElement(instrument, "tripleoscillator", params)
        add_common_instrument_nodes(
            it,
            filter_cutoff=filter_cutoff,
            filter_resonance=filter_resonance,
            envelope=True,
            attack=attack,
            decay=decay,
            sustain=sustain,
            release=release,
        )

    pattern = ET.SubElement(
        track,
        "pattern",
        {"type": "1", "muted": "0", "steps": "16", "name": name, "pos": "0", "len": str(SONG_LEN)},
    )
    for key, pos, length, velocity in notes:
        ET.SubElement(
            pattern,
            "note",
            {"key": str(key), "vol": str(velocity), "pos": str(pos), "pan": "0", "len": str(length)},
        )


def bar_pos(bar: int, tick: int = 0) -> int:
    return (bar - 1) * TICKS_PER_BAR + tick


def chord_notes(chord: tuple[int, ...], bar: int, hits: tuple[tuple[int, int, int], ...]) -> list[tuple[int, int, int, int]]:
    result: list[tuple[int, int, int, int]] = []
    for tick, length, velocity in hits:
        for index, key in enumerate(chord):
            # 高音稍弱一点，让和弦更像键盘分层。
            result.append((key, bar_pos(bar, tick), length, max(35, velocity - index * 3)))
    return result


def build_music() -> dict[str, list[tuple[int, int, int, int]]]:
    tracks: dict[str, list[tuple[int, int, int, int]]] = {
        "Kick": [],
        "Snare": [],
        "Closed Hat": [],
        "Open Hat": [],
        "Crash": [],
        "Funk Bass": [],
        "Electric Keys": [],
        "Funk Stab": [],
        "Lead": [],
    }

    progression = [
        ((53, 56, 60, 63, 67), 41, 48),  # Fm9
        ((49, 53, 56, 60, 63), 37, 44),  # Dbmaj9
        ((51, 55, 58, 60, 65), 39, 46),  # Eb13-ish
        ((48, 52, 55, 58, 63), 36, 43),  # C7#9
        ((53, 56, 60, 63, 67), 41, 48),
        ((46, 49, 53, 56, 60), 34, 41),  # Bbm9
        ((49, 53, 56, 60, 63), 37, 44),
        ((48, 52, 55, 58, 63), 36, 43),
    ] * 2

    chord_hits = ((0, 28, 69), (72, 18, 74), (126, 24, 66), (174, 16, 78))
    stab_hits = ((30, 10, 82), (84, 10, 76), (138, 10, 84))

    for bar, (chord, root, fifth) in enumerate(progression, start=1):
        tracks["Electric Keys"].extend(chord_notes(chord, bar, chord_hits))
        top = chord[-3:]
        tracks["Funk Stab"].extend(chord_notes(top, bar, stab_hits))

        next_root = progression[bar % len(progression)][1]
        approach = next_root - 1 if next_root >= root else next_root + 1
        bass_pattern = [
            (root, 0, 22, 105),
            (root + 12, 30, 12, 84),
            (fifth, 48, 18, 92),
            (root + 7, 78, 12, 80),
            (root, 96, 20, 102),
            (root + 10, 126, 12, 76),
            (fifth, 144, 12, 88),
            (approach, 174, 12, 82),
        ]
        if bar in (4, 8, 12, 16):
            bass_pattern[-2:] = [(root + 12, 150, 10, 91), (approach, 180, 8, 88)]
        for key, tick, length, velocity in bass_pattern:
            tracks["Funk Bass"].append((key, bar_pos(bar, tick), length, velocity))

        kick_patterns = (
            (0, 42, 96, 132),
            (0, 36, 90, 120, 168),
            (0, 54, 96, 138),
            (0, 30, 84, 120, 174),
        )
        for tick in kick_patterns[(bar - 1) % 4]:
            tracks["Kick"].append((57, bar_pos(bar, tick), -192, 105 if tick in (0, 96) else 90))

        for tick, velocity in ((48, 108), (144, 112), (132, 42 if bar % 2 else 52)):
            tracks["Snare"].append((57, bar_pos(bar, tick), -192, velocity))

        for index, tick in enumerate((0, 27, 48, 75, 96, 123, 144, 171)):
            velocity = 63 if index % 2 == 0 else 48
            if bar >= 9:
                velocity += 6
            tracks["Closed Hat"].append((57, bar_pos(bar, tick), -192, velocity))
        if bar not in (4, 8, 12, 16):
            tracks["Open Hat"].append((57, bar_pos(bar, 171), -192, 61))

    for bar in (1, 9, 16):
        tracks["Crash"].append((57, bar_pos(bar, 0), -192, 90 if bar != 16 else 105))

    # 主旋律只在中后段出现，留出前四小节建立律动。
    melody = {
        5: [(72, 24, 18), (75, 48, 12), (77, 66, 30), (72, 108, 12), (70, 132, 24), (68, 168, 18)],
        6: [(68, 12, 18), (70, 36, 12), (72, 54, 18), (75, 84, 30), (73, 126, 18), (72, 156, 24)],
        7: [(72, 18, 18), (68, 48, 12), (67, 66, 12), (65, 84, 30), (68, 126, 18), (72, 156, 18)],
        8: [(70, 12, 18), (67, 42, 12), (64, 60, 12), (65, 78, 30), (63, 126, 18), (64, 156, 24)],
        13: [(72, 18, 12), (75, 36, 12), (77, 54, 24), (80, 90, 18), (77, 126, 12), (75, 144, 30)],
        14: [(73, 12, 18), (72, 36, 12), (70, 54, 24), (68, 90, 18), (70, 120, 12), (72, 138, 36)],
        15: [(75, 18, 18), (72, 48, 12), (68, 66, 18), (70, 96, 18), (72, 126, 12), (73, 144, 30)],
        16: [(72, 12, 12), (70, 30, 12), (68, 48, 12), (67, 66, 12), (65, 84, 24), (68, 120, 18), (72, 144, 36)],
    }
    for bar, phrases in melody.items():
        for key, tick, length in phrases:
            tracks["Lead"].append((key, bar_pos(bar, tick), length, 88 if length < 24 else 96))

    return tracks


def build_project() -> None:
    root = ET.Element(
        "lmms-project",
        {"type": "song", "version": "1.0", "creator": "LMMS", "creatorversion": "1.2.2"},
    )
    ET.SubElement(
        root,
        "head",
        {
            "timesig_denominator": "4",
            "bpm": str(BPM),
            "masterpitch": "0",
            "mastervol": "38",
            "timesig_numerator": "4",
        },
    )
    song = ET.SubElement(root, "song")
    container = ET.SubElement(
        song,
        "trackcontainer",
        {"visible": "1", "width": "1200", "height": "820", "type": "song", "x": "20", "y": "20", "maximized": "0", "minimized": "0"},
    )

    tracks = build_music()

    add_track(container, "Kick", tracks["Kick"], volume=72, sample="drums/kick01.ogg")
    add_track(container, "Snare", tracks["Snare"], volume=66, sample="drums/snare_acoustic01.ogg")
    add_track(container, "Closed Hat", tracks["Closed Hat"], volume=47, pan=-12, sample="drums/hihat_closed02.ogg")
    add_track(container, "Open Hat", tracks["Open Hat"], volume=38, pan=18, sample="drums/hihat_opened01.ogg")
    add_track(container, "Crash", tracks["Crash"], volume=42, pan=8, sample="drums/crash01.ogg")

    add_track(
        container,
        "Funk Bass",
        tracks["Funk Bass"],
        volume=56,
        pan=0,
        synth={"wavetype0": 3, "wavetype1": 2, "wavetype2": 0, "vol0": 100, "vol1": 48, "vol2": 28, "coarse2": -12},
        filter_cutoff=2200,
        filter_resonance=1.35,
        attack=0.0,
        decay=0.12,
        sustain=0.65,
        release=0.08,
    )
    add_track(
        container,
        "Electric Keys",
        tracks["Electric Keys"],
        volume=37,
        pan=-12,
        synth={"wavetype0": 0, "wavetype1": 2, "wavetype2": 0, "vol0": 100, "vol1": 24, "vol2": 36, "finel1": -8, "finer1": 8},
        filter_cutoff=7800,
        filter_resonance=0.75,
        attack=0.005,
        decay=0.42,
        sustain=0.34,
        release=0.28,
    )
    add_track(
        container,
        "Funk Stab",
        tracks["Funk Stab"],
        volume=28,
        pan=16,
        synth={"wavetype0": 2, "wavetype1": 3, "wavetype2": 0, "vol0": 82, "vol1": 35, "vol2": 18},
        filter_cutoff=5200,
        filter_resonance=1.1,
        attack=0.0,
        decay=0.09,
        sustain=0.08,
        release=0.06,
    )
    add_track(
        container,
        "Lead",
        tracks["Lead"],
        volume=30,
        pan=8,
        synth={"wavetype0": 2, "wavetype1": 0, "wavetype2": 3, "vol0": 72, "vol1": 58, "vol2": 16, "finel1": -11, "finer1": 11},
        filter_cutoff=6900,
        filter_resonance=1.45,
        attack=0.018,
        decay=0.22,
        sustain=0.46,
        release=0.2,
    )

    mixer = ET.SubElement(song, "fxmixer", {"visible": "0", "width": "647", "height": "332", "x": "9", "y": "441", "maximized": "0", "minimized": "0"})
    master = ET.SubElement(mixer, "fxchannel", {"num": "0", "muted": "0", "volume": "1", "name": "Master", "soloed": "0"})
    ET.SubElement(master, "fxchain", {"numofeffects": "0", "enabled": "0"})
    ET.SubElement(song, "ControllerRackView", {"visible": "0", "width": "258", "height": "173", "x": "664", "y": "444", "maximized": "0", "minimized": "0"})
    ET.SubElement(song, "pianoroll", {"visible": "0", "width": "900", "height": "600", "x": "20", "y": "20", "maximized": "0", "minimized": "0"})
    ET.SubElement(song, "automationeditor", {"visible": "0", "width": "640", "height": "400", "x": "356", "y": "129", "maximized": "0", "minimized": "0"})
    ET.SubElement(song, "projectnotes", {"visible": "0", "width": "408", "height": "394", "x": "9", "y": "16", "maximized": "0", "minimized": "0"})
    ET.SubElement(song, "timeline", {"lp0pos": "0", "lp1pos": str(SONG_LEN), "lpstate": "0"})
    ET.SubElement(song, "controllers")

    tree = ET.ElementTree(root)
    ET.indent(tree, space="  ")
    ROOT.mkdir(parents=True, exist_ok=True)
    tree.write(PROJECT_PATH, encoding="utf-8", xml_declaration=True)
    text = PROJECT_PATH.read_text(encoding="utf-8")
    PROJECT_PATH.write_text(text.replace("<?xml version='1.0' encoding='utf-8'?>", "<?xml version=\"1.0\"?>\n<!DOCTYPE lmms-project>"), encoding="utf-8")
    print(PROJECT_PATH)


if __name__ == "__main__":
    build_project()
