import math

verts, groups = [], {}
def reset(): verts.clear(); groups.clear()

def _normal(a, b, c):
    u = (b[0]-a[0], b[1]-a[1], b[2]-a[2]); v = (c[0]-a[0], c[1]-a[1], c[2]-a[2])
    return (u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0])

def add_face(mat, base, idxs, pts, hint):
    """Thêm mặt, tự đảo chiều để pháp tuyến hướng về `hint` (hướng ra ngoài)."""
    n = _normal(pts[idxs[0]], pts[idxs[1]], pts[idxs[2]])
    if n[0]*hint[0] + n[1]*hint[1] + n[2]*hint[2] < 0:
        idxs = idxs[::-1]
    groups.setdefault(mat, []).append([base+i+1 for i in idxs])

# ---- shape lồi (hộp thuôn) ----
def box(mat, x0, x1, y0, y1, z0, z1, top_shrink_x=0.0, top_z_front=0.0, top_z_back=0.0):
    pts = [(x0,y0,z0),(x1,y0,z0),(x1,y0,z1),(x0,y0,z1),
           (x0+top_shrink_x,y1,z0+top_z_back),(x1-top_shrink_x,y1,z0+top_z_back),
           (x1-top_shrink_x,y1,z1-top_z_front),(x0+top_shrink_x,y1,z1-top_z_front)]
    base = len(verts); verts.extend(pts)
    c = [sum(p[k] for p in pts)/8 for k in range(3)]
    for f in [[0,1,2,3],[4,5,6,7],[0,1,5,4],[1,2,6,5],[2,3,7,6],[3,0,4,7]]:
        m = [sum(pts[i][k] for i in f)/len(f) - c[k] for k in range(3)]
        add_face(mat, base, f, pts, m)

# ---- tam giác hoá đa giác (ear clipping), poly = [(z,y)] ----
def area2(p):
    return sum(p[i][0]*p[(i+1)%len(p)][1]-p[(i+1)%len(p)][0]*p[i][1] for i in range(len(p)))
def triangulate(poly):
    idx = list(range(len(poly)))
    if area2(poly) < 0: idx.reverse()
    tris, guard = [], 0
    cr = lambda a,b,c: (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
    while len(idx) > 3 and guard < 10000:
        guard += 1
        for k in range(len(idx)):
            i0, i1, i2 = idx[k-1], idx[k], idx[(k+1) % len(idx)]
            a, b, c = poly[i0], poly[i1], poly[i2]
            cv = cr(a,b,c)
            if abs(cv) < 1e-9:
                idx.pop(k); break                      # đỉnh thẳng hàng -> bỏ
            if cv < 0: continue
            inside = False
            for j in idx:
                if j in (i0,i1,i2): continue
                p = poly[j]
                if cr(a,b,p) > 1e-9 and cr(b,c,p) > 1e-9 and cr(c,a,p) > 1e-9:
                    inside = True; break
            if inside: continue
            tris.append((i0,i1,i2)); idx.pop(k); break
    tris.append(tuple(idx))
    return tris

def extrude_x(mat, poly, x0, x1):
    """Đùn đa giác (z,y) dọc trục X từ x0 đến x1."""
    n = len(poly)
    pts = [(x0, y, z) for (z, y) in poly] + [(x1, y, z) for (z, y) in poly]
    base = len(verts); verts.extend(pts)
    sgn = 1 if area2(poly) > 0 else -1
    for t in triangulate(poly):
        add_face(mat, base, list(t), pts, (-1,0,0))
        add_face(mat, base, [t[0]+n, t[1]+n, t[2]+n], pts, (1,0,0))
    for i in range(n):
        j = (i+1) % n
        ez, ey = poly[j][0]-poly[i][0], poly[j][1]-poly[i][1]
        add_face(mat, base, [i, j, n+j, n+i], pts, (0, -ez*sgn, ey*sgn))

# ---- đường viền thân (nhìn ngang), z = dọc xe, y = cao ----
Y0, Y_TOP, ZC, R, YC = 0.28, 0.92, 1.35, 0.45, 0.38
def arch(zc):
    p = [(zc-R, Y0)]
    for a in (180,150,120,90,60,30,0):
        p.append((zc + R*math.cos(math.radians(a)), YC if a in (0,180) else YC + R*math.sin(math.radians(a))))
    p.append((zc+R, Y0))
    return p

def outline(with_arches):
    p = [(-2.10, Y0)]
    if with_arches: p += arch(-ZC) + arch(ZC)
    p += [(2.10, Y0), (2.10, 0.74), (1.85, Y_TOP), (-1.85, Y_TOP), (-2.10, 0.78)]
    return p

MATS = {"Body":(0.85,0.12,0.12),"Cabin":(0.55,0.78,0.92),"Bumper":(0.15,0.15,0.17),
        "Light":(1.00,0.95,0.60),"Tail":(0.75,0.05,0.05),"Well":(0.06,0.06,0.07)}

def write(name, mats):
    with open(f"{name}.mtl","w") as f:
        for m in mats:
            r,g,b = MATS[m]; f.write(f"newmtl {m}\nKd {r} {g} {b}\nKa 0 0 0\nKs 0 0 0\nd 1\n\n")
    with open(f"{name}.obj","w") as f:
        f.write(f"mtllib {name}.mtl\no {name}\n")
        for v in verts: f.write(f"v {v[0]:.4f} {v[1]:.4f} {v[2]:.4f}\n")
        for m in mats:
            if m in groups:
                f.write(f"g {m}\nusemtl {m}\n")
                for fc in groups[m]: f.write("f "+" ".join(map(str,fc))+"\n")
    print(name, "verts:", len(verts), "faces:", sum(len(v) for v in groups.values()))

# ================= THÂN XE CÓ HỐC BÁNH =================
reset()
with_arch, solid = outline(True), outline(False)
extrude_x("Body",  with_arch,  0.55,  0.90)      # tấm hông phải (có hốc)
extrude_x("Body",  with_arch, -0.90, -0.55)      # tấm hông trái
extrude_x("Body",  solid,     -0.55,  0.55)      # phần giữa liền
for zc in (-ZC, ZC):                             # tấm tối lót trong hốc bánh
    well = arch(zc)
    extrude_x("Well", well,  0.55,  0.58)
    extrude_x("Well", well, -0.58, -0.55)

box("Cabin",  -0.80,0.80,0.92,1.45,-1.10,1.05, top_shrink_x=0.12, top_z_front=0.35, top_z_back=0.25)
box("Bumper", -0.92,0.92,0.30,0.50,2.10,2.18)
box("Bumper", -0.92,0.92,0.30,0.50,-2.18,-2.10)
box("Light",  -0.78,-0.40,0.58,0.72,2.10,2.15); box("Light",0.40,0.78,0.58,0.72,2.10,2.15)
box("Tail",   -0.78,-0.40,0.58,0.72,-2.15,-2.10); box("Tail",0.40,0.78,0.58,0.72,-2.15,-2.10)
write("LowPolyCar_Body", ["Body","Well","Cabin","Bumper","Light","Tail"])
