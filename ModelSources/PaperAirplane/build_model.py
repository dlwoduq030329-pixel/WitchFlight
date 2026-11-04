"""Run with Blender in background. Builds the folded model, FBX and a preview."""
import bpy
import json
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets' / 'Art' / 'PaperAirplane'
SOURCE = Path(__file__).resolve().parent
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

paper = bpy.data.materials.new('Paper_White')
paper.diffuse_color = (0.93, 0.95, 0.98, 1)
paper.use_nodes = True
bsdf = next((node for node in paper.node_tree.nodes if node.type == 'BSDF_PRINCIPLED'), None)
if bsdf is None:
    bsdf = paper.node_tree.nodes.new('ShaderNodeBsdfPrincipled')
    output = paper.node_tree.nodes.new('ShaderNodeOutputMaterial')
    paper.node_tree.links.new(bsdf.outputs['BSDF'], output.inputs['Surface'])
bsdf.inputs['Base Color'].default_value = paper.diffuse_color
bsdf.inputs['Roughness'].default_value = 0.78

# Author in Unity coordinates: +Z nose, +Y up; convert to Blender Z-up.
vertices, faces = [], []
def panel(points):
    start = len(vertices)
    vertices.extend((x, -z, y) for x, y, z in points)
    faces.append(tuple(range(start, len(vertices))))

for side in (-1, 1):
    nose = (0, 0.025, 1.25)
    fold = (side * 0.225, 0.065, -0.77)
    root = (side * 0.028, 0.015, -0.70)
    tip = (side * 0.88, 0.045, -0.94)
    shoulder = (side * 0.64, 0.062, -0.43)
    keel = (side * 0.003, -0.36, -0.61)
    # Wide wing, narrow inner fold, and downward folded central keel.
    panels = [[nose, shoulder, tip, fold], [nose, fold, root], [nose, root, keel]]
    for points in panels:
        panel(list(reversed(points)) if side == 1 else points)

mesh = bpy.data.meshes.new('PaperAirplane')
mesh.from_pydata(vertices, [], faces)
mesh.update()
plane = bpy.data.objects.new('PaperAirplane', mesh)
bpy.context.collection.objects.link(plane)
bpy.context.view_layer.objects.active = plane
plane.select_set(True)
plane.data.materials.append(paper)
solid = plane.modifiers.new('Paper thickness', 'SOLIDIFY')
solid.thickness = 0.002
solid.offset = 0
solid.use_even_offset = True
bpy.ops.object.modifier_apply(modifier=solid.name)
triangulate = plane.modifiers.new('Export triangles', 'TRIANGULATE')
bpy.ops.object.modifier_apply(modifier=triangulate.name)
for poly in plane.data.polygons:
    poly.use_smooth = False

# Split corner normals retain crisp folds; keep winding consistent with normals.
v, n, uv, triangles = [], [], [], []
for face in plane.data.polygons:
    start = len(v)
    for index in face.vertices:
        p = plane.data.vertices[index].co
        v.append({'x': p.x, 'y': p.z, 'z': -p.y})
        n.append({'x': face.normal.x, 'y': face.normal.z, 'z': -face.normal.y})
        uv.append({'x': p.x / 1.8 + 0.5, 'y': (-p.y + 0.94) / 2.19})
    triangles.extend((start, start + 1, start + 2))
(SOURCE / 'mesh.json').write_text(json.dumps({'vertices': v, 'normals': n, 'uv': uv, 'triangles': triangles}), encoding='utf-8')
bpy.ops.export_scene.fbx(filepath=str(OUT / 'PaperAirplane.fbx'), use_selection=True,
    object_types={'MESH'}, axis_forward='-Z', axis_up='Y', add_leaf_bones=False,
    mesh_smooth_type='FACE', bake_anim=False)

# Preview camera and lights are only in the editable Blender source, not the FBX.
def point_at(obj, point):
    obj.rotation_euler = (Vector(point) - obj.location).to_track_quat('-Z', 'Y').to_euler()
bpy.ops.object.camera_add(location=(3.25, 4.9, 2.5))
camera = bpy.context.object
camera.name = 'Preview Camera'
point_at(camera, (0, -0.05, -0.04))
camera.data.type = 'ORTHO'
camera.data.ortho_scale = 2.9
bpy.context.scene.camera = camera
for name, pos, power, size in [('Softbox', (-3, -4, 6), 700, 4), ('Fill', (4, 2, 3), 160, 5)]:
    bpy.ops.object.light_add(type='AREA', location=pos)
    light = bpy.context.object
    light.name = name
    light.data.energy = power
    light.data.shape = 'DISK'
    light.data.size = size
    point_at(light, (0, 0, 0))
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 48
scene.world.color = (0.22, 0.35, 0.55)
scene.world.use_nodes = True
scene.world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.23, 0.40, 0.68, 1)
scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.45
scene.render.resolution_x = 1200
scene.render.resolution_y = 900
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.render.filepath = str(SOURCE / 'PaperAirplane_Preview.png')
scene.view_settings.view_transform = 'AgX'
bpy.ops.object.select_all(action='DESELECT')
plane.select_set(True)
bpy.context.view_layer.objects.active = plane
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / 'PaperAirplane.blend'))
bpy.ops.render.render(write_still=True)
print('Paper airplane exported:', len(plane.data.vertices), 'vertices,', len(plane.data.polygons), 'triangles')
