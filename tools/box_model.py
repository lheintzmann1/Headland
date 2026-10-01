#!/usr/bin/env python3
"""Builds a generated base model of boxes (a .glb, and its Blockbench .bbmodel beside it) from a JSON spec.

The spec is a tree of named nodes, as docs/MODELING.md wants them (a `root`, moving parts named after their roles):

    { "root": { "children": [
        { "name": "lift", "at": [0, 1.2, 0], "boxes": [ { "size": [0.16, 0.3, 0.3], "material": "dark_steel" } ],
          "children": [ { "name": "tilt", "at": [0, -1.1, 2.7], "rot": [20, 0, 0], "boxes": [...] } ] } ] } }

`at` is a node's pivot in its parent's space (meters, +x left, +y up, +z forward), `rot` its turn in degrees about x,
then y, then z. Each box is its own child node (`<node>_0`, `<node>_1`…), `size` [x, y, z] centered on `at` [x, y, z]
in the node's space. Materials: paint (the machine's color), paint_75/80/85 (darker shades of it), steel, dark_steel,
tire, rim, tread, fill (the load's color), tarp (a dark green-grey canvas), wood (a pallet's boards).

    python3 tools/box_model.py spec.json game/assets/models/<category>/<id>.glb
"""
import json
import math
import struct
import sys
import uuid

UNITS = 16.0  # Blockbench units per meter

MATERIALS = {
    "paint": ([1.0, 1.0, 1.0], 0.0, 0.75),
    "paint_75": ([0.5225, 0.5225, 0.5225], 0.0, 0.75),
    "paint_80": ([0.6038, 0.6038, 0.6038], 0.0, 0.75),
    "paint_85": ([0.6939, 0.6939, 0.6939], 0.0, 0.75),
    "steel": ([0.0890, 0.0946, 0.1005], 0.4, 0.6),
    "dark_steel": ([0.0272, 0.0301, 0.0331], 0.0, 0.85),
    "tire": ([0.0085, 0.0085, 0.0085], 0.0, 0.95),
    "rim": ([0.3424, 0.3185, 0.2633], 0.3, 0.5),
    "tread": ([0.0085, 0.0085, 0.0085], 0.0, 0.95),
    "fill": ([1.0, 1.0, 1.0], 0.0, 0.9),
    "tarp": ([0.0452, 0.0513, 0.0356], 0.0, 0.95),
    "wood": ([0.2462, 0.1499, 0.0723], 0.0, 0.9),
}

# Each face of a box: its normal, tangent and the corners (as signs of the half size), counterclockwise from outside.
FACES = [
    ((1, 0, 0), (0, 0, -1), [(1, -1, 1), (1, -1, -1), (1, 1, -1), (1, 1, 1)]),
    ((-1, 0, 0), (0, 0, 1), [(-1, -1, -1), (-1, -1, 1), (-1, 1, 1), (-1, 1, -1)]),
    ((0, 1, 0), (1, 0, 0), [(-1, 1, 1), (1, 1, 1), (1, 1, -1), (-1, 1, -1)]),
    ((0, -1, 0), (1, 0, 0), [(-1, -1, -1), (1, -1, -1), (1, -1, 1), (-1, -1, 1)]),
    ((0, 0, 1), (1, 0, 0), [(-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)]),
    ((0, 0, -1), (-1, 0, 0), [(1, -1, -1), (-1, -1, -1), (-1, 1, -1), (1, 1, -1)]),
]
UVS = [(0, 1), (1, 1), (1, 0), (0, 0)]


def quaternion(rot):
    """Degrees about x, then y, then z, as a glTF quaternion [x, y, z, w]."""
    def axis(i, deg):
        h = math.radians(deg) / 2
        q = [0.0, 0.0, 0.0, math.cos(h)]
        q[i] = math.sin(h)
        return q

    def mul(a, b):
        ax, ay, az, aw = a
        bx, by, bz, bw = b
        return [aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
                aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz]

    # Turned about x first: the z turn is outermost.
    return mul(axis(2, rot[2]), mul(axis(1, rot[1]), axis(0, rot[0])))


class Glb:
    def __init__(self):
        self.doc = {"asset": {"version": "2.0", "generator": "Headland base model generator"}, "scene": 0,
                    "scenes": [{"nodes": [0]}], "nodes": [], "meshes": [], "materials": [], "accessors": [],
                    "bufferViews": [], "buffers": [{"byteLength": 0}]}
        self.bin = bytearray()
        self.materials = {}
        self.meshes = {}

    def view(self, data, stride, target):
        while len(self.bin) % 4:
            self.bin.append(0)
        view = {"buffer": 0, "byteOffset": len(self.bin), "byteLength": len(data), "target": target}
        if stride:
            view["byteStride"] = stride
        self.bin += data
        self.doc["bufferViews"].append(view)
        return len(self.doc["bufferViews"]) - 1

    def accessor(self, values, kind, count, bounds=False):
        comps = {"VEC2": 2, "VEC3": 3, "VEC4": 4}[kind]
        data = struct.pack(f"<{len(values)}f", *values)
        acc = {"bufferView": self.view(data, comps * 4, 34962), "componentType": 5126, "count": count, "type": kind}
        if bounds:
            acc["min"] = [min(values[i::comps]) for i in range(comps)]
            acc["max"] = [max(values[i::comps]) for i in range(comps)]
        self.doc["accessors"].append(acc)
        return len(self.doc["accessors"]) - 1

    def material(self, name):
        if name not in self.materials:
            color, metallic, roughness = MATERIALS[name]
            self.doc["materials"].append({"name": name, "pbrMetallicRoughness": {
                "baseColorFactor": color + [1.0], "metallicFactor": metallic, "roughnessFactor": roughness}})
            self.materials[name] = len(self.doc["materials"]) - 1
        return self.materials[name]

    def box(self, size, material):
        key = (tuple(size), material)
        if key in self.meshes:
            return self.meshes[key]
        half = [s / 2 for s in size]
        pos, nor, tan, uv, idx = [], [], [], [], []
        for normal, tangent, corners in FACES:
            base = len(pos) // 3
            for corner, (u, v) in zip(corners, UVS):
                pos += [c * h for c, h in zip(corner, half)]
                nor += list(normal)
                tan += list(tangent) + [1.0]
                uv += [u, v]
            idx += [base, base + 1, base + 2, base, base + 2, base + 3]
        attributes = {"POSITION": self.accessor(pos, "VEC3", 24, bounds=True), "TANGENT": self.accessor(tan, "VEC4", 24),
                      "NORMAL": self.accessor(nor, "VEC3", 24), "TEXCOORD_0": self.accessor(uv, "VEC2", 24)}
        indices = struct.pack(f"<{len(idx)}H", *idx)
        self.doc["accessors"].append({"bufferView": self.view(indices, 0, 34963), "componentType": 5123, "count": len(idx), "type": "SCALAR"})
        self.doc["meshes"].append({"primitives": [{"attributes": attributes, "indices": len(self.doc["accessors"]) - 1,
                                                   "material": self.material(material), "mode": 4}]})
        self.meshes[key] = len(self.doc["meshes"]) - 1
        return self.meshes[key]

    def node(self, spec):
        index = len(self.doc["nodes"])
        node = {"name": spec["name"]}
        self.doc["nodes"].append(node)
        if any(spec.get("at", [0, 0, 0])):
            node["translation"] = [float(v) for v in spec["at"]]
        if any(spec.get("rot", [0, 0, 0])):
            node["rotation"] = quaternion(spec["rot"])
        children = []
        for i, b in enumerate(spec.get("boxes", [])):
            child = {"name": f"{spec['name']}_{i}", "mesh": self.box(b["size"], b.get("material", "paint"))}
            if any(b.get("at", [0, 0, 0])):
                child["translation"] = [float(v) for v in b["at"]]
            self.doc["nodes"].append(child)
            children.append(len(self.doc["nodes"]) - 1)
        for c in spec.get("children", []):
            children.append(self.node(c))
        if children:
            node["children"] = children
        return index

    def write(self, path):
        while len(self.bin) % 4:
            self.bin.append(0)
        self.doc["buffers"][0]["byteLength"] = len(self.bin)
        js = json.dumps(self.doc, separators=(",", ":")).encode()
        js += b" " * (-len(js) % 4)
        body = struct.pack("<II", len(js), 0x4E4F534A) + js + struct.pack("<II", len(self.bin), 0x004E4942) + bytes(self.bin)
        with open(path, "wb") as f:
            f.write(struct.pack("<III", 0x46546C67, 2, 12 + len(body)) + body)


def bbmodel(spec, name):
    """The Blockbench project: a group per node (pivot and turn), a cube per box, placed as the nodes rest unturned."""
    elements = []
    faces = {f: {"uv": [0, 0, 16, 16], "texture": None} for f in ("north", "east", "south", "west", "up", "down")}

    def units(v):
        return [round(x * UNITS, 4) for x in v]

    def group(s, parent_at):
        at = [p + a for p, a in zip(parent_at, s.get("at", [0, 0, 0]))]
        children = []
        for i, b in enumerate(s.get("boxes", [])):
            center = [a + o for a, o in zip(at, b.get("at", [0, 0, 0]))]
            lo = [c - h / 2 for c, h in zip(center, b["size"])]
            hi = [c + h / 2 for c, h in zip(center, b["size"])]
            element = {"name": f"{s['name']}_{i}", "box_uv": False, "rescale": False, "locked": False, "render_order": "default",
                       "allow_mirror_modeling": True, "from": units(lo), "to": units(hi), "autouv": 0, "color": 0,
                       "origin": units(center), "rotation": [0, 0, 0], "faces": faces, "type": "cube", "uuid": str(uuid.uuid4())}
            elements.append(element)
            children.append(element["uuid"])
        children += [group(c, at) for c in s.get("children", [])]
        return {"name": s["name"], "origin": units(at), "color": 0, "uuid": str(uuid.uuid4()), "export": True, "isOpen": False,
                "locked": False, "visibility": True, "autouv": 0, "rotation": [float(r) for r in s.get("rot", [0, 0, 0])],
                "children": children}

    outliner = [group(spec, [0, 0, 0])]
    return {"meta": {"format_version": "4.5", "model_format": "free", "box_uv": False}, "name": name, "model_identifier": "",
            "visible_box": [1, 1, 0], "resolution": {"width": 16, "height": 16}, "elements": elements, "outliner": outliner,
            "textures": []}


def main():
    if len(sys.argv) != 3 or not sys.argv[2].endswith(".glb"):
        sys.exit(__doc__)
    spec = json.load(open(sys.argv[1]))["root"]
    spec["name"] = "root"
    glb = Glb()
    glb.node(spec)
    glb.write(sys.argv[2])
    name = sys.argv[2].rsplit("/", 1)[-1][:-4]
    json.dump(bbmodel(spec, name), open(sys.argv[2][:-4] + ".bbmodel", "w"), separators=(",", ":"))


if __name__ == "__main__":
    main()
