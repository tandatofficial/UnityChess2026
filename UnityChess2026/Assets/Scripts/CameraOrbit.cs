using UnityEngine;

public class CameraOrbit : MonoBehaviour
{
    [Header("Cài đặt Xoay Đa Chiều")]
    public float sensitivityX = 300f; // Tốc độ xoay trái / phải
    public float sensitivityY = 300f; // Tốc độ xoay lên / xuống
    public float minimumY = 15f;      // Giới hạn ngửa lên (Gần sát mặt bàn)
    public float maximumY = 85f;      // Giới hạn chúi xuống (Nhìn gần như thẳng đứng từ trên xuống)

    [Header("Cài đặt Zoom")]
    public float zoomSpeed = 800f;     // Tốc độ phóng to / thu nhỏ
    public float minZoom = -6f;        // Tiến sát bàn cờ nhất
    public float maxZoom = -35f;       // Lùi xa bàn cờ nhất

    private Camera cam;
    private float rotationX = 0f;
    private float rotationY = 55f;     // Góc nghiêng mặc định ban đầu là 55 độ

    void Start()
    {
        cam = GetComponentInChildren<Camera>();

        // Tự động lấy góc xoay hiện tại của Pivot khi mới vào game để không bị giật hình
        Vector3 currentAngles = transform.eulerAngles;
        rotationX = currentAngles.y;
        rotationY = currentAngles.x;
    }

    void Update()
    {
        // --- 1. TÍNH NĂNG XOAY ĐA CHIỀU (Giữ chuột phải) ---
        if (Input.GetMouseButton(1))
        {
            // Di chuột sang Trái/Phải -> Thay đổi góc quay quanh trục đứng (Y)
            rotationX += Input.GetAxis("Mouse X") * sensitivityX * Time.deltaTime;

            // Di chuột lên/xuống -> Thay đổi góc nghiêng lên/xuống (X)
            // Dấu trừ (-) giúp đồng bộ: Di chuột LÊN thì camera nghiêng XUỐNG để nhìn bàn cờ
            rotationY -= Input.GetAxis("Mouse Y") * sensitivityY * Time.deltaTime;

            // QUAN TRỌNG: Khóa góc quay lên/xuống lại, không cho camera lật nhào dưới mặt bàn
            rotationY = Mathf.Clamp(rotationY, minimumY, maximumY);

            // Áp dụng đồng thời cả góc xoay trái/phải và lên/xuống vào CameraPivot
            transform.rotation = Quaternion.Euler(rotationY, rotationX, 0);
        }

        // --- 2. TÍNH NĂNG ZOOM THẲNG TRỤC MẮT NHÌN ---
        if (cam != null)
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0.0f)
            {
                Vector3 localPos = cam.transform.localPosition;
                localPos.z += scroll * zoomSpeed * Time.deltaTime;
                localPos.z = Mathf.Clamp(localPos.z, maxZoom, minZoom);
                cam.transform.localPosition = localPos;
            }
        }
    }
}