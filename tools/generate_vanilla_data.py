"""Generates the vanilla data files that FLScan II uses with --assume-vanilla-files-exist.

Usage: python generate_vanilla_data.py <unmodified Freelancer install> [output folder, default: ../src]

Writes:
- vanilla_inis.txt: a stripped-down copy of every vanilla INI. Each file starts with a line "@@ <path relative to
  DATA>", followed by its sections with only the settings in KEPT_KEYS (sections without any are left out).
  FLScan II reads these copies in its parse stage when a mod doesn't ship a vanilla INI, so the nicknames that file
  defines are still known. When a parse function starts reading a new key, add it to KEPT_KEYS and regenerate.
- vanilla_hardpoints.txt: one line per vanilla model (.cmp, .3db, .dfm): its path relative to DATA, followed by its
  hardpoint names, separated by tabs. Used for hardpoint checks on models a mod doesn't ship.
"""
import os
import struct
import sys

# Settings read by the parse stage (FLScan II's Parse*/Load* functions), lowercase
KEPT_KEYS = {
    'nickname', 'name',                        # nicknames of most sections, [fuse] name, [Style] name
    'da_archetype', 'mesh',                    # models (ships, solars, equipment, bodyparts)
    'shield_type',                             # [ShieldGenerator]
    'category', 'ship',                        # [Good]
    'base', 'marketgood',                      # [BaseGood]
    'system', 'file',                          # universe.ini [Base] and [System]
    'msg',                                     # voices
    'room', 'prop', 'cart',                    # petaldb.ini [ObjectTable]
    'member',                                  # shipclasses.ini
    'level', 'npc_class', 'ship_archetype',    # npcships.ini
    'affiliation', 'formation', 'npc_ship',    # faction_prop.ini
    'tex_shape', 'shape_name',                 # shape files of asteroid fields and nebulas
}

# Settings of which the parse stage only reads the first value
FIRST_VALUE_ONLY = {'marketgood'}

MODEL_EXTENSIONS = ('.cmp', '.3db', '.dfm')


def read_bini(buf):
    """Returns [(section, [(key, [values])])] of a binary INI."""
    str_table = struct.unpack_from('<i', buf, 8)[0]

    def string(offset):
        end = buf.index(b'\0', str_table + offset)
        return buf[str_table + offset:end].decode('latin-1')

    sections = []
    p = 12
    while p < str_table:
        name_offset, num_entries = struct.unpack_from('<HH', buf, p)
        p += 4
        entries = []
        for _ in range(num_entries):
            key_offset, num_values = struct.unpack_from('<HB', buf, p)
            p += 3
            values = []
            for _ in range(num_values):
                value_type = buf[p]
                if value_type == 1:
                    values.append(str(struct.unpack_from('<i', buf, p + 1)[0]))
                elif value_type == 2:
                    values.append('%g' % struct.unpack_from('<f', buf, p + 1)[0])
                elif value_type == 3:
                    values.append(string(struct.unpack_from('<i', buf, p + 1)[0]))
                else:
                    raise ValueError('unknown value type %d at 0x%x' % (value_type, p))
                p += 5
            entries.append((string(key_offset), values))
        sections.append((string(name_offset), entries))
    return sections


def read_text_ini(buf):
    """Returns [(section, [(key, [values])])] of a text INI, parsed like FLScan II does."""
    sections = []
    for line in buf.decode('latin-1').splitlines():
        line = line.split(';', 1)[0].strip()
        if not line or line.startswith('#'):
            continue
        if line.startswith('[') and line.endswith(']'):
            sections.append((line[1:-1], []))
        else:
            if not sections:
                sections.append(('ROOT', []))
            key, _, value = line.partition('=')
            sections[-1][1].append((key.strip(), [v.strip() for v in value.split(',')] if value.strip() else []))
    return sections


def read_ini(path):
    with open(path, 'rb') as f:
        buf = f.read()
    return read_bini(buf) if buf[:4] == b'BINI' else read_text_ini(buf)


def read_hardpoints(path):
    """Returns the hardpoint names of a UTF model, found like FLScan II's UtfFile does."""
    with open(path, 'rb') as f:
        buf = f.read()
    signature, version = struct.unpack_from('<ii', buf, 0)
    if signature != 0x20465455 or version != 0x101:
        raise ValueError('not a UTF file')
    node_block = struct.unpack_from('<i', buf, 8)[0]
    string_block = struct.unpack_from('<i', buf, 24)[0]

    hardpoints = []
    stack = [(0, '+')]  # (node offset in the node block, path of the parent nodes)
    while stack:
        node, depth = stack.pop()
        offset = node_block + node
        peer, name_offset, flags = struct.unpack_from('<iii', buf, offset)
        child = struct.unpack_from('<i', buf, offset + 16)[0]
        start = string_block + name_offset
        name = buf[start:buf.index(b'\0', start)].decode('latin-1')
        lower = depth.lower()
        if lower.endswith('hardpoints+fixed+') or lower.endswith('hardpoints+revolute+'):
            hardpoints.append(name)
        if peer > 0:
            stack.append((peer, depth))
        if child > 0 and flags == 0x10:
            stack.append((child, depth + name + '+'))
    return hardpoints


def main():
    install = sys.argv[1]
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src')
    data = os.path.join(install, 'DATA')

    inis, models = [], []
    for root, dirs, files in os.walk(data):
        dirs.sort()
        for f in sorted(files):
            rel = os.path.relpath(os.path.join(root, f), data).replace('/', '\\').lower()
            if rel.endswith('.ini'):
                inis.append(rel)
            elif rel.endswith(MODEL_EXTENSIONS):
                models.append(rel)

    lines = []
    for rel in inis:
        kept = []
        for section, entries in read_ini(os.path.join(data, rel)):
            entries = [(k, v[:1] if k.lower() in FIRST_VALUE_ONLY else v) for k, v in entries if k.lower() in KEPT_KEYS]
            if entries:
                kept.append((section, entries))
        if not kept:
            continue
        lines.append('@@ ' + rel)
        for section, entries in kept:
            lines.append('[%s]' % section)
            lines.extend('%s = %s' % (k, ', '.join(v)) for k, v in entries)
    with open(os.path.join(out, 'vanilla_inis.txt'), 'w', encoding='latin-1', newline='\r\n') as f:
        f.write('\n'.join(lines) + '\n')

    lines = []
    for rel in models:
        try:
            hardpoints = read_hardpoints(os.path.join(data, rel))
        except (ValueError, struct.error) as e:
            print('skipped %s: %s' % (rel, e))
            continue
        lines.append('\t'.join([rel] + hardpoints))
    with open(os.path.join(out, 'vanilla_hardpoints.txt'), 'w', encoding='latin-1', newline='\r\n') as f:
        f.write('\n'.join(lines) + '\n')

    print('%d INIs, %d models' % (len(inis), len(models)))


if __name__ == '__main__':
    main()
