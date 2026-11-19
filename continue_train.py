# chạy tiếp từ checkpoint gần nhất
.\.venv\Scripts\mlagents-learn.exe configs/parking_config.yaml --run-id=parking01 --resume --time-scale=20 --torch-device cpu

# chạy lại từ đầu (xoá kết quả cũ)
.\.venv\Scripts\mlagents-learn.exe configs/parking_config.yaml --run-id=parking01 --force --time-scale=20 --torch-device cpu