#!/usr/bin/env python3
"""
Scarica i modelli InsightFace `buffalo_sc` e li prepara per FaceQuest.

Cosa fa, per ciascuno dei due modelli:
  1. fissa la forma di ingresso (SCRFD: 1x3x288x384, ArcFace: 1x3x112x112);
  2. cuce all'inizio del grafo la normalizzazione dei pixel, cosi' il codice C#
     passa direttamente pixel RGB in [0,1]:
        SCRFD  : (x*255 - 127.5) / 128   ->  x*1.9921875 - 0.99609375
        ArcFace: (x*255 - 127.5) / 127.5 ->  x*2        - 1
  3. rende statiche le forme di uscita (shape inference + onnxsim);
  4. VERIFICA il risultato: esegue modello originale (con normalizzazione
     esterna) e modello convertito (con pixel grezzi) con onnxruntime su
     immagini casuali e confronta le uscite. Se la differenza supera la soglia
     lo script si ferma e NON scrive nulla.

Uso (dalla radice del progetto):
    pip install onnx onnxruntime onnxsim numpy
    python Tools/prepara_modelli_facequest.py
    python Tools/prepara_modelli_facequest.py --zip buffalo_sc.zip   # zip gia' scaricato

I pesi di InsightFace sono per uso di ricerca non commerciale: leggere la loro
licenza prima di qualsiasi altro uso. Vedi MODELS.md.
"""
import argparse
import io
import os
import sys
import urllib.request
import zipfile

import numpy as np
import onnx
from onnx import TensorProto, helper, numpy_helper

URL = "https://github.com/deepinsight/insightface/releases/download/v0.7/buffalo_sc.zip"

# nome nello zip -> (file di destinazione, forma ingresso, divisore std)
MODELLI = {
    "det_500m.onnx": ("scrfd_500m_384x288.onnx", (1, 3, 288, 384), 128.0),
    "w600k_mbf.onnx": ("arcface_mbf_112.onnx", (1, 3, 112, 112), 127.5),
}
MEDIA = 127.5
SOGLIA = 1e-3  # differenza massima ammessa tra originale e convertito


def converti(modello_bytes, forma, std):
    """Restituisce il modello convertito (oggetto onnx.ModelProto)."""
    m = onnx.load_model_from_string(modello_bytes)
    g = m.graph

    # ingresso reale (esclude eventuali initializer elencati tra gli input)
    nomi_init = {i.name for i in g.initializer}
    ingressi = [i for i in g.input if i.name not in nomi_init]
    if len(ingressi) != 1:
        raise RuntimeError(f"attesi 1 ingresso, trovati {len(ingressi)}")
    vecchio = ingressi[0]
    nome_vecchio = vecchio.name

    # forma fissa
    dims = vecchio.type.tensor_type.shape.dim
    if len(dims) != len(forma):
        raise RuntimeError(f"rango ingresso {len(dims)} != {len(forma)}")
    for d, v in zip(dims, forma):
        d.ClearField("dim_param")
        d.dim_value = v

    # nuovo ingresso 'image' + normalizzazione cucita davanti
    scala = np.array([255.0 / std], dtype=np.float32)
    bias = np.array([-MEDIA / std], dtype=np.float32)
    g.initializer.extend([
        numpy_helper.from_array(scala, "norm_scale"),
        numpy_helper.from_array(bias, "norm_bias"),
    ])
    nuovo = helper.make_tensor_value_info("image", TensorProto.FLOAT, list(forma))
    g.input.remove(vecchio)
    g.input.insert(0, nuovo)
    mul = helper.make_node("Mul", ["image", "norm_scale"], ["image_s"], name="norm_mul")
    add = helper.make_node("Add", ["image_s", "norm_bias"], [nome_vecchio + "_norm"], name="norm_add")
    for n in g.node:  # chi leggeva il vecchio ingresso legge ora quello normalizzato
        for k, nome in enumerate(n.input):
            if nome == nome_vecchio:
                n.input[k] = nome_vecchio + "_norm"
    g.node.insert(0, add)
    g.node.insert(0, mul)

    # forme di uscita statiche
    for o in g.output:
        del o.type.tensor_type.shape.dim[:]
    del g.value_info[:]
    m = onnx.shape_inference.infer_shapes(m)
    try:
        from onnxsim import simplify
        m, ok = simplify(m)
        if not ok:
            print("  attenzione: onnxsim non ha validato la semplificazione", file=sys.stderr)
    except ImportError:
        print("  onnxsim non installato: salto la semplificazione", file=sys.stderr)
    onnx.checker.check_model(m)

    for o in m.graph.output:
        for d in o.type.tensor_type.shape.dim:
            if d.dim_value <= 0:
                raise RuntimeError(f"uscita {o.name} con forma non statica dopo la conversione")
    return m


def verifica(originale_bytes, convertito, forma, std):
    """Confronta originale (normalizzazione esterna) e convertito (pixel grezzi)."""
    import onnxruntime as ort

    opz = ort.SessionOptions()
    opz.log_severity_level = 3
    s_orig = ort.InferenceSession(originale_bytes, opz, providers=["CPUExecutionProvider"])
    s_conv = ort.InferenceSession(convertito.SerializeToString(), opz, providers=["CPUExecutionProvider"])
    in_orig = s_orig.get_inputs()[0].name
    rng = np.random.default_rng(1234)
    peggiore = 0.0
    for _ in range(3):
        x = rng.random(forma, dtype=np.float32)  # pixel in [0,1]
        out_c = s_conv.run(None, {"image": x})
        xn = (x * 255.0 - MEDIA) / std
        out_o = s_orig.run(None, {in_orig: xn.astype(np.float32)})
        libere = list(range(len(out_o)))
        for c in out_c:  # accoppia per forma: l'ordine/nome delle uscite puo' differire
            for k in libere:
                o = out_o[k]
                if o.shape == c.shape:
                    peggiore = max(peggiore, float(np.max(np.abs(o - c))))
                    libere.remove(k)
                    break
            else:
                raise RuntimeError(f"nessuna uscita originale con forma {c.shape}")
    return peggiore


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--zip", help="percorso di un buffalo_sc.zip gia' scaricato")
    radice = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
    ap.add_argument("--dest", default=os.path.join(radice, "Assets", "FaceQuest", "Resources"),
                    help="cartella di destinazione (default: Assets/FaceQuest/Resources)")
    a = ap.parse_args()

    if a.zip:
        dati = open(a.zip, "rb").read()
    else:
        print("Scarico", URL)
        dati = urllib.request.urlopen(URL, timeout=120).read()
    z = zipfile.ZipFile(io.BytesIO(dati))

    pronti = {}
    for nome_zip, (dest, forma, std) in MODELLI.items():
        print(f"- {nome_zip} -> {dest}")
        originale = z.read(nome_zip)
        m = converti(originale, forma, std)
        diff = verifica(originale, m, forma, std)
        print(f"  verifica: differenza massima {diff:.2e} (soglia {SOGLIA:.0e})")
        if not diff <= SOGLIA:
            print("ERRORE: il modello convertito non coincide con l'originale. Non scrivo nulla.", file=sys.stderr)
            return 1
        pronti[dest] = m

    os.makedirs(a.dest, exist_ok=True)
    for dest, m in pronti.items():
        percorso = os.path.join(a.dest, dest)
        onnx.save(m, percorso)
        print("scritto", percorso)
    print("Fatto. Apri Unity: importera' i modelli (i file .meta sono gia' nel repository).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
