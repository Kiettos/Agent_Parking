# Thiết kế Môi trường & Gameplay

## 1. Phong cách (Style)
- Low-poly 3D, góc nhìn từ trên xuống (Top-down).
- Màu sắc tương phản để AI dễ nhận biết (Raycast LayerMask).

## 2. Các thành phần (Components)
- **Đường (R):** Màu xám đậm.
- **Chỗ đỗ (P):** Màu vàng nhạt.
- **Vật cản (Tường, Cột đèn, Bụi cây):** Màu xám/Đen (Có gắn Layer `Obstacle`).
- **Spawn (S):** Điểm xuất phát (Màu xanh lá).
- **Target (T):** Đích đến (Màu đỏ).

## 3. Thông số Vật lý của Xe (CarAgent)
- Khối lượng (Mass): 1
- Tốc độ tối đa (Move Speed): 10 m/s
- Tốc độ xoay (Turn Speed): 150 độ/s