using UnityEngine;

public class Contr : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 5f;            // скорость ходьбы
    public float runSpeed = 10f;            // максимальная скорость бега
    public float jumpForce = 5f;            // сила прыжка

    [Header("Energy for Running")]
    public float maxEnergy = 5f;            // максимальная энергия бега (в секундах)
    public float energyConsumptionRate = 1f;  // сколько энергии тратится в секунду при беге
    public float energyRecoveryRate = 0.5f;   // скорость восстановления энергии в секунду

    [Header("Camera & Mouse")]
    public Transform cameraTransform;       // для вращения камеры
    public float mouseSensitivity = 2f;     // чувствительность мыши

    private Rigidbody rb;
    private float rotationX = 0f;

    private bool isGrounded = false;

    private float currentEnergy;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (cameraTransform == null)
            cameraTransform = Camera.main.transform;

        currentEnergy = maxEnergy;
    }

    private void Update()
    {
        HandleMovement();
        HandleJump();
        HandleCameraRotation();
        HandleEnergy();
    }

    private void HandleMovement()
    {
        float moveHorizontal = Input.GetAxis("Horizontal");
        float moveVertical = Input.GetAxis("Vertical");

        Vector3 moveDirection = (transform.forward * moveVertical + transform.right * moveHorizontal).normalized;

        bool isRunning = Input.GetKey(KeyCode.LeftShift) && currentEnergy > 0 && (moveHorizontal != 0 || moveVertical != 0);

        float speed = isRunning ? runSpeed : walkSpeed;

        Vector3 desiredVelocity = moveDirection * speed;
        Vector3 velocity = rb.linearVelocity;

        // Обновляем горизонтальную скорость, вертикальная скорость остается без изменений
        velocity.x = desiredVelocity.x;
        velocity.z = desiredVelocity.z;
        rb.linearVelocity = velocity;

        // Затраты энергии при беге
        if (isRunning)
        {
            currentEnergy -= energyConsumptionRate * Time.deltaTime;
            if (currentEnergy < 0)
                currentEnergy = 0;
        }
    }

    private void HandleJump()
    {
        if (Input.GetKeyDown(KeyCode.Space) && isGrounded)
        {
            // Сброс вертикальной скорости перед прыжком (чтобы прыжок был более предсказуемым)
            Vector3 velocity = rb.linearVelocity;
            velocity.y = 0;
            rb.linearVelocity = velocity;

            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            isGrounded = false; // чтобы не прыгать в воздухе до следующего касания
        }
    }

    private void HandleCameraRotation()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        // Вращение персонажа по горизонтали
        transform.Rotate(0, mouseX, 0);

        // Вращение камеры по вертикали с ограничениями
        rotationX -= mouseY;
        rotationX = Mathf.Clamp(rotationX, -90f, 90f);
        cameraTransform.localRotation = Quaternion.Euler(rotationX, 0, 0);
    }

    private void HandleEnergy()
    {
        // Если не бежим и энергия не полная — восстанавливаем энергию
        if (!Input.GetKey(KeyCode.LeftShift) && currentEnergy < maxEnergy)
        {
            currentEnergy += energyRecoveryRate * Time.deltaTime;
            if (currentEnergy > maxEnergy)
                currentEnergy = maxEnergy;
        }
    }

    // Проверяем касание земли через столкновения
    private void OnCollisionStay(Collision collision)
    {
        // Простая проверка - если есть контакт снизу с чем-либо, считаем, что на земле
        foreach (ContactPoint contact in collision.contacts)
        {
            if (Vector3.Dot(contact.normal, Vector3.up) > 0.5f)
            {
                isGrounded = true;
                return;
            }
        }
    }

    private void OnCollisionExit(Collision collision)
    {
        // При выходе из столкновения не гарантируем сразу, что на земле — переопределим isGrounded через Update,
        // но чтобы не сделать прыжки в воздухе, оставим логику простой (если хотите, можно улучшить)
        isGrounded = false;
    }
}