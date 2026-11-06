#!/usr/bin/env python3
"""Makes the runtime voices: the three Piper voices with int8 weights, the decoder kept at float32.

The runtime voice (PluginData/Runtime/voices) uses smaller copies of the build voices in audio/voices/. ONNX Runtime
quantize_dynamic turns the weights into 8 bit (QUInt8). The decoder, the last part of the model that makes the
waveform, is excluded and stays float32, because quantizing it too gives static in the pauses (measured about 13 dB
more noise floor for davefx and claude). Size per voice goes from about 63 MB to about 22 MB.

The decoder is found by structure, so it works for exports with and without node names: it starts at the last Conv
before the first ConvTranspose, and runs to the end of the graph (ONNX nodes are in topological order).

With --compare <piper python>, every voice is also rendered with Piper noise off (so only the model differs), and the
noise floor in the pauses (RMS of the quietest 10 percent of 20 ms frames) is printed for the original and the copy.

Needs: onnx, onnxruntime, numpy (not in audio/.venv by default: pip install onnx onnxruntime numpy).
Usage: python3 -I audio/quantize_runtime_voices.py [--voices audio/voices] [--out audio/build/runtime_voices]
                                                    [--compare audio/.venv/bin/python]
Then: ./fetch_runtime.sh stages them with Piper for package.sh and build.sh."""
import argparse
import hashlib
import logging
import shutil
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
VOICES = ["es_ES-davefx-medium", "es_MX-claude-high", "es_MX-ald-medium"]
TEXT = "lortnoC, im ollidacob es ah odapacse. oL oev. aV aicah al allitocse."


def decoder_nodes(model):
    ns = list(model.graph.node)
    first_up = next(i for i, n in enumerate(ns) if n.op_type == "ConvTranspose")
    start = max(i for i in range(first_up) if ns[i].op_type == "Conv")
    return [n.name for n in ns[start:] if n.name], ns[start].name


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def render(piper_py, model):
    raw = subprocess.run([piper_py, "-m", "piper", "-m", str(model), "--output-raw", "--noise-scale", "0",
                          "--noise-w-scale", "0"], input=TEXT.encode(), capture_output=True, check=True).stdout
    return raw


def floor_db(raw, sr=22050):
    import numpy as np
    x = np.frombuffer(raw, dtype=np.int16).astype(np.float64) / 32768.0
    f = int(sr * 0.02)
    rms = np.sqrt(np.array([np.mean(x[i:i + f] ** 2) for i in range(0, len(x) - f, f)]) + 1e-12)
    q = np.sort(rms)[: max(1, len(rms) // 10)]
    return 20 * np.log10(np.mean(q))


def main():
    ap = argparse.ArgumentParser(description="Quantize the Piper voices for the Lonapse runtime voice.")
    ap.add_argument("--voices", type=Path, default=REPO / "audio" / "voices")
    ap.add_argument("--out", type=Path, default=REPO / "audio" / "build" / "runtime_voices")
    ap.add_argument("--compare", metavar="PIPER_PYTHON", help="python that has piper-tts, to measure the noise floor")
    args = ap.parse_args()

    try:
        import onnx
        import onnxruntime
        from onnxruntime.quantization import QuantType, quantize_dynamic
    except ImportError as e:
        sys.exit("ERROR: %s (pip install onnx onnxruntime numpy)" % e)
    logging.disable(logging.WARNING)  # quantize_dynamic logs a shape warning for every node
    print("onnx %s, onnxruntime %s" % (onnx.__version__, onnxruntime.__version__))

    args.out.mkdir(parents=True, exist_ok=True)
    for v in VOICES:
        src = args.voices / (v + ".onnx")
        if not src.is_file() or not Path(str(src) + ".json").is_file():
            sys.exit("ERROR: missing %s(.json)" % src)
        dst = args.out / src.name
        dec, first = decoder_nodes(onnx.load(str(src)))
        quantize_dynamic(str(src), str(dst), weight_type=QuantType.QUInt8, nodes_to_exclude=dec)
        shutil.copy(str(src) + ".json", str(dst) + ".json")
        print("%s: decoder from %s, %d nodes kept float32, %.1f MB -> %.1f MB" % (
            v, first, len(dec), src.stat().st_size / 1e6, dst.stat().st_size / 1e6))
        print("  sha256 %s" % sha256(dst))
        if args.compare:
            print("  pause noise floor: original %.1f dB, quantized %.1f dB" % (
                floor_db(render(args.compare, src)), floor_db(render(args.compare, dst))))
    print("Wrote %d voices to %s" % (len(VOICES), args.out))


if __name__ == "__main__":
    main()
