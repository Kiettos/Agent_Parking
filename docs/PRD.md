# PRD: Hệ thống Xe tự động đỗ xe (Parking Agent)

## 1. Mục tiêu dự án
Xây dựng một AI sử dụng Reinforcement Learning (Học tăng cường) có khả năng tự lái xe, tìm đường và đỗ vào bãi đỗ xe một cách chính xác mà không cần con người can thiệp.

## 2. Đối tượng hướng tới
- Sinh viên/Nhóm làm đồ án AI.
- Những người muốn tìm hiểu về Unity ML-Agents.

## 3. Tính năng cốt lõi (Core Features)
- Môi trường mô phỏng 3D có vật lý (Rigidbody).
- Bản đồ đa dạng: Vuông góc, Chéo, Song song, Hỗn hợp (Ziczac) được sinh tự động từ file JSON.
- Hỗ trợ nhiều Agent (20 xe) chạy trên cùng một map (Shared Policy).
- AI tự học cách né vật cản (Raycast) và đỗ đúng hướng.

## 4. Yêu cầu kỹ thuật
- SPS (Steps Per Second) đạt mức tối thiểu 1500 trên CPU Core i5/Ryzen 5.
- Thời gian train hội tụ (đỗ thành công >80%) dưới 24 giờ.