using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using DG.Tweening; // Нужен DOTween
using Unity.Cinemachine; // Для новой версии Cinemachine (или Cinemachine для старой)

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 8f;
    public float rotationSpeed = 15f;
    public float gravity = -20f;
    public float groundCheckDistance = 0.3f;
    public LayerMask groundLayer;

    [Header("Jump")]
    public float jumpImpulse = 10f;
    public float jumpCancelRate = 10f; // Для регулируемой высоты прыжка
    public float jumpButtonHoldTime = 0.2f; // Окно доп. ускорения

    [Header("Hover (Flight)")]
    public float hoverGravity = -2f;
    public float hoverHoldThreshold = 0.3f; // Сколько держать ПКМ для активации
    public float staminaMax = 100f;
    public float staminaDepleteRate = 20f;
    private float currentStamina;
    private bool isHovering = false;
    private float hoverTimer = 0f;

    [Header("Camera & Zoom")]
    public CinemachineOrbitalFollow orbitalFollow;
    public float minZoomRadius = 2f;
    public float maxZoomRadius = 10f;
    public float zoomSensitivity = 1f;
    private float targetZoomRadius;

    //[Header("Audio & FX")]
    //[SerializeField] private FMODUnity.EventReference moveSound;
    //[SerializeField] private FMODUnity.EventReference jumpSound;
    //[SerializeField] private FMODUnity.EventReference hoverSound;
    //private FMOD.Studio.EventInstance moveSoundInstance;

    // Internal state
    private CharacterController controller;
    private GameInput inputActions;
    private Vector3 velocity;
    private bool isGrounded;
    private Transform camTransform;
    private float currentJumpHoldTimer;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        inputActions = new GameInput();
        camTransform = Camera.main.transform;
        currentStamina = staminaMax;
        targetZoomRadius = orbitalFollow.Radius;

        // Инициализация звука бега
        //moveSoundInstance = FMODUnity.RuntimeManager.CreateInstance(moveSound);

        Cursor.visible = false;
    }

    private void OnEnable() => inputActions.Enable();
    private void OnDisable() => inputActions.Disable();

    private void Update()
    {
        HandleGroundCheck();
        HandleMovement();
        HandleJump();
        HandleHover();
        HandleZoom();
        ApplyGravity();
        UpdateFMODParameters();
    }

    private void HandleGroundCheck()
    {
        isGrounded = Physics.CheckSphere(transform.position + Vector3.up * 0.1f, groundCheckDistance, groundLayer);
        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f; // Прижимаем к земле
            currentStamina = Mathf.MoveTowards(currentStamina, staminaMax, Time.deltaTime * 50f);
        }
    }

    private void HandleMovement()
    {
        Vector2 input = inputActions.Player.Move.ReadValue<Vector2>();
        Vector3 direction = new Vector3(input.x, 0, input.y).normalized;

        if (direction.magnitude >= 0.1f)
        {
            // Рассчитываем направление относительно камеры
            float targetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + camTransform.eulerAngles.y;
            Vector3 moveDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;

            controller.Move(moveDir * moveSpeed * Time.deltaTime);

            // Плавный поворот персонажа
            Quaternion targetRotation = Quaternion.Euler(0, targetAngle, 0);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);

            // Управление звуком бега
            if (isGrounded)
            {
                PLAY_MoveSound();
            }
        }
        else
        {
            STOP_MoveSound();
        }
    }

    private void HandleJump()
    {
        if (inputActions.Player.Jump.WasPressedThisFrame() && isGrounded)
        {
            velocity.y = jumpImpulse;
            currentJumpHoldTimer = jumpButtonHoldTime;
            //FMODUnity.RuntimeManager.PlayOneShot(jumpSound, transform.position);
            // Добавь здесь эффект пыли прыжка
        }

        // Variable Jump Height: если держим пробел, гравитация временно меньше воздействует
        if (inputActions.Player.Jump.IsPressed() && currentJumpHoldTimer > 0)
        {
            currentJumpHoldTimer -= Time.deltaTime;
        }
        else if (velocity.y > 0)
        {
            // Если отпустили раньше времени, резко гасим вертикальный импульс
            velocity.y -= jumpCancelRate * Time.deltaTime;
        }
    }

    private void HandleHover()
    {
        bool hoverInput = inputActions.Player.Hover.IsPressed();

        if (hoverInput && !isGrounded && currentStamina > 0)
        {
            hoverTimer += Time.deltaTime;
            if (hoverTimer >= hoverHoldThreshold && !isHovering)
            {
                StartHover();
            }
        }
        else
        {
            StopHover();
        }

        if (isHovering)
        {
            currentStamina -= staminaDepleteRate * Time.deltaTime;
            if (currentStamina <= 0) StopHover();
        }
    }

    private void StartHover()
    {
        if (isHovering) return;
        isHovering = true;
        velocity.y = 0; // Сброс вертикального ускорения

        // Визуальный эффект (например, раздувание или наклон) через DOTween
        transform.DOScale(new Vector3(1.1f, 0.9f, 1.1f), 0.2f).SetLoops(2, LoopType.Yoyo);

        // FMOD: включение звука парения
        //FMODUnity.RuntimeManager.PlayOneShot(hoverSound, transform.position);
    }

    private void StopHover()
    {
        isHovering = false;
        hoverTimer = 0;
    }

    private void HandleZoom()
    {
        // Читаем Vector2 (так как в настройках стоит Vector2)
        Vector2 scrollValue = inputActions.Player.Zoom.ReadValue<Vector2>();
        float scrollY = scrollValue.y;

        if (Mathf.Abs(scrollY) > 0.01f)
        {
            // Нормализуем значение (скролл может выдавать 120 или 1 в зависимости от мыши)
            float step = Mathf.Sign(scrollY) * zoomSensitivity;
            targetZoomRadius -= step;
            targetZoomRadius = Mathf.Clamp(targetZoomRadius, minZoomRadius, maxZoomRadius);
        }

        // Лерп для плавности (используем Mathf.Lerp или DOTween)
        orbitalFollow.Radius = Mathf.Lerp(orbitalFollow.Radius, targetZoomRadius, Time.deltaTime * 5f);
    }

    private void ApplyGravity()
    {
        float currentGrav = isHovering ? hoverGravity : gravity;
        velocity.y += currentGrav * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    private void UpdateFMODParameters()
    {
        // Определение поверхности через Raycast
        RaycastHit hit;
        if (Physics.Raycast(transform.position, Vector3.down, out hit, 1.5f))
        {
            // В FMOD должен быть параметр "Surface" (0 - трава, 1 - камень и т.д.)
            int surfaceType = 0;
            if (hit.collider.CompareTag("Stone")) surfaceType = 1;

            //moveSoundInstance.setParameterByName("Surface", surfaceType);
        }
    }

    private void PLAY_MoveSound()
    {
        //FMOD.Studio.PLAYBACK_STATE state;
        //moveSoundInstance.getPlaybackState(out state);
        //if (state != FMOD.Studio.PLAYBACK_STATE.PLAYING) moveSoundInstance.start();
        return;
    }

    private void STOP_MoveSound()
    {
        //moveSoundInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        return;
    }
}