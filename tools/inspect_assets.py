"""Read local Unity metadata; never modifies game assets."""
import sys
sys.path.insert(0, str(__import__('pathlib').Path(__file__).resolve().parents[2] / '.tools/python-packages'))
import UnityPy
env = UnityPy.load(sys.argv[1])
if '--mount' in sys.argv:
    from UnityPy.helpers.MeshHelper import MeshHandler
    for obj in env.objects:
        if obj.type.name != 'Mesh' or obj.read().m_Name not in ('lever_independent_brake', 'CabooseInterior'):
            continue
        mesh = MeshHandler(obj.read()); mesh.process()
        if obj.read().m_Name == 'lever_independent_brake':
            points = [v for v in mesh.m_Vertices if -0.02 < v[0] < 0.035 and abs(v[1]) < 0.016]
        else:
            points = [v for v in mesh.m_Vertices if abs(v[0] + 0.8859) < 0.022 and abs(v[1] - 2.4713) < 0.022 and v[2] > -4.86]
        print(obj.read().m_Name)
        for v in sorted(set(tuple(round(x, 6) for x in v) for v in points)):
            print(v)
if '--de2' in sys.argv or '--s060' in sys.argv:
    from UnityPy.helpers.MeshHelper import MeshHandler
    def visit_de2(go, path=''):
        path += '/' + go.m_Name
        for entry in go.m_Component:
            reader = entry.component.deref()
            if reader.type.name == 'Transform':
                tr = reader.read()
                if ('brake' in path.lower() if '--s060' in sys.argv else ('BrakeIndependent' in path or 'brake_independent' in path)):
                    print(path, 'position', tr.m_LocalPosition, 'rotation', tr.m_LocalRotation, 'scale', tr.m_LocalScale)
                    for ent in go.m_Component:
                        rr = ent.component.deref()
                        if rr.type.name in ('MeshRenderer', 'MeshFilter', 'BoxCollider', 'CapsuleCollider', 'SphereCollider', 'MeshCollider'):
                            print(rr.type.name, rr.read_typetree())
                        if '--s060' in sys.argv and go.m_Name == 'C_IndependentBrake' and rr.type.name == 'MonoBehaviour':
                            import struct
                            base = rr.read(check_read=False)
                            if base.m_Script.read().m_Name == 'Lever':
                                raw = rr.get_raw_data()
                                for label, offset in [('notch', -40), ('drag', -28), ('limitHit', -16)]:
                                    file_id, path_id = struct.unpack_from('<iq', raw, len(raw) + offset)
                                    clip = rr.assets_file.objects.get(path_id) if file_id == 0 else None
                                    print('S060 AUDIO', label, file_id, path_id, clip.type.name if clip else None,
                                          clip.read().m_Name if clip else None)
                for child in tr.m_Children:
                    visit_de2(child.read().m_GameObject.read(), path)
    for obj in env.objects:
        if obj.type.name == 'GameObject' and obj.read().m_Name == ('LocoS060_Interior' if '--s060' in sys.argv else 'LocoDE2_Interior'):
            visit_de2(obj.read())
        if obj.type.name == 'Mesh' and obj.read().m_Name == 'lever_independent_brake':
            mesh = MeshHandler(obj.read()); mesh.process()
            print('DE2 MESH BOUNDS', [(min(v[i] for v in mesh.m_Vertices), max(v[i] for v in mesh.m_Vertices)) for i in range(3)])
if '--layers' in sys.argv:
    for obj in env.objects:
        if obj.type.name == 'TagManager':
            print(obj.read_typetree())
if '--resources' in sys.argv:
    for obj in env.objects:
        if obj.type.name == 'ResourceManager':
            for key, ptr in obj.read().m_Container:
                if any(s in key.lower() for s in ('key', 'shovel', 'hammer', 'flash', 'lighter', 'caboose')):
                    print(key, ptr)
if '--valve' in sys.argv:
    from UnityPy.helpers.MeshHelper import MeshHandler
    for obj in env.objects:
        if obj.type.name == 'Mesh' and obj.read().m_Name == 'CabooseInterior':
            mesh = MeshHandler(obj.read())
            mesh.process()
            vertices = [v for v in mesh.m_Vertices if -1.2 < v[0] < -0.2 and 2.4 < v[1] < 2.95 and -5 < v[2] < -4.5]
            print('VALVE VERTICES', len(vertices))
            print('BOUNDS', [(min(v[i] for v in vertices), max(v[i] for v in vertices)) for i in range(3)])
            for v in sorted(set(tuple(round(x, 4) for x in v) for v in vertices if v[2] > -4.80)):
                print(v)
for obj in env.objects:
    if obj.type.name in ('GameObject', 'Mesh', 'MonoScript'):
        data = obj.read()
        name = getattr(data, 'm_Name', '')
        if any(s in name.lower() for s in sys.argv[2:]):
            print(obj.assets_file.name, obj.path_id, obj.type.name, name)
            if obj.type.name == 'GameObject':
                for component in data.m_Component:
                    try:
                        c = component.component.deref().read(check_read=False)
                    except Exception:
                        continue
                    if c.object_reader.type.name == 'Transform':
                        print(' TRANSFORM', c.m_LocalPosition, c.m_LocalRotation)
                    elif c.object_reader.type.name == 'MonoBehaviour':
                        try:
                            print(' SCRIPT', c.m_Script.read().m_Name)
                        except Exception:
                            pass
if '--tree' in sys.argv:
    def walk(go, indent=''):
        print(indent + go.m_Name)
        for entry in go.m_Component:
            reader = entry.component.deref()
            if reader.type.name == 'Transform':
                tr = reader.read()
                print(indent, tr.m_LocalPosition, tr.m_LocalRotation)
                for child in tr.m_Children:
                    walk(child.read().m_GameObject.read(), indent + '  ')
    for obj in env.objects:
        if obj.type.name == 'GameObject' and obj.read().m_Name in ('CarCabooseRed_interior', 'KeyCaboose'):
            walk(obj.read())
