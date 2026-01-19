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
    public float acceleration = 50f; // Насколько быстро разгоняется
    public float deceleration = 40f; // Насколько быстро тормозит
    public float rotationSpeed = 15f;
    public float gravity = -25f;
    public float groundCheckDistance = 0.3f;
    public LayerMask groundLayer;

    [Header("Jump Tuning")]
    [Tooltip("Сила начального рывка вверх")]
    public float jumpImpulse = 12f;
    [Tooltip("Насколько сильно падает скорость, если отпустить пробел раньше")]
    public float jumpCancelRate = 40f;
    [Tooltip("Время (сек), в течение которого можно держать пробел для макс. высоты")]
    public float jumpButtonHoldTime = 0.25f;

    [Header("Juice (Squash & Stretch)")]
    public Transform visualRoot; // ПУСТОЙ ОБЪЕКТ, ГДЕ ЛЕЖИТ МОДЕЛЬ
    public float squashAmount = 0.6f;
    public float stretchAmount = 1.4f;
    public float juiceDuration = 0.2f;

    [Header("Impact & Shake")]
    public CinemachineImpulseSource impulseSource;
    public float fallShakeThreshold = -15f; // Скорость падения, после которой будет тряска

    [Header("Hover (Flight)")]
    public float hoverGravity = -3f;
    public float hoverHoldThreshold = 0.25f;
    public float staminaMax = 100f;
    public float staminaDepleteRate = 25f;
    private float currentStamina;

    [Header("Camera & Zoom")]
    public CinemachineOrbitalFollow orbitalFollow;
    public float minZoomRadius = 3f;
    public float maxZoomRadius = 12f;
    public float zoomSensitivity = 1.5f;
    private float targetZoomRadius;

    //[Header("Audio & FX")]
    //[SerializeField] private FMODUnity.EventReference moveSound;
    //[SerializeField] private FMODUnity.EventReference jumpSound;
    //[SerializeField] private FMODUnity.EventReference hoverSound;
    //private FMOD.Studio.EventInstance moveSoundInstance;

    // Internal state
    private CharacterController controller;
    private GameInput inputActions;
    private Vector3 moveVelocity; // Горизонтальная скорость
    private Vector3 verticalVelocity; // Вертикальная скорость
    private bool isGrounded;
    private bool isHovering;
    private float hoverTimer;
    private float currentJumpHoldTimer;
    private Transform camTransform;
    private float lastYVelocity; // Для отслеживания силы удара об землю

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        inputActions = new GameInput();
        camTransform = Camera.main.transform;
        currentStamina = staminaMax;
        targetZoomRadius = orbitalFollow.Radius;

        // Фиксация курсора
        LockCursor(true);
    }

    private void OnEnable() => inputActions.Enable();
    private void OnDisable() => inputActions.Disable();

    private void Update()
    {
        HandleCursor();
        HandleGroundCheck();
        HandleMovement();
        HandleJump();
        HandleHover();
        HandleZoom();
        ApplyGravity();
    }

    private void LockCursor(bool locked)
    {
        Cursor.visible = !locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
    }

    private void HandleCursor()
    {
        if (Keyboard.current.escapeKey.wasPressedThisFrame) LockCursor(false);
        if (Mouse.current.leftButton.wasPressedThisFrame && !Cursor.visible) LockCursor(true);
    }

    private void HandleGroundCheck()
    {
        bool wasGrounded = isGrounded;
        isGrounded = Physics.CheckSphere(transform.position + Vector3.up * 0.1f, groundCheckDistance, groundLayer);

        if (isGrounded && !wasGrounded)
        {
            OnLand();
        }

        if (isGrounded && verticalVelocity.y < 0)
        {
            verticalVelocity.y = -2f;
            currentStamina = Mathf.MoveTowards(currentStamina, staminaMax, Time.deltaTime * 40f);
        }
    }

    private void HandleMovement()
    {
        Vector2 input = inputActions.Player.Move.ReadValue<Vector2>();

        // Рассчитываем целевой вектор направления
        Vector3 targetDirection = new Vector3(input.x, 0, input.y).normalized;
        Vector3 worldMoveDir = Vector3.zero;

        if (targetDirection.magnitude >= 0.1f)
        {
            float targetAngle = Mathf.Atan2(targetDirection.x, targetDirection.z) * Mathf.Rad2Deg + camTransform.eulerAngles.y;
            worldMoveDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;

            // Плавный поворот персонажа
            Quaternion targetRotation = Quaternion.Euler(0, targetAngle, 0);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);

            // Разгон
            moveVelocity = Vector3.MoveTowards(moveVelocity, worldMoveDir * moveSpeed, acceleration * Time.deltaTime);
        }
        else
        {
            // Торможение
            moveVelocity = Vector3.MoveTowards(moveVelocity, Vector3.zero, deceleration * Time.deltaTime);
        }

        controller.Move(moveVelocity * Time.deltaTime);
    }

    private void HandleJump()
    {
        if (inputActions.Player.Jump.WasPressedThisFrame() && isGrounded)
        {
            verticalVelocity.y = jumpImpulse;
            currentJumpHoldTimer = jumpButtonHoldTime;

            ApplySquashStretch(stretchAmount, squashAmount); // Растягиваем при прыжке
            // FMOD: Play Jump Sound
        }

        if (inputActions.Player.Jump.IsPressed() && currentJumpHoldTimer > 0)
        {
            currentJumpHoldTimer -= Time.deltaTime;
        }
        else if (verticalVelocity.y > 0)
        {
            // Эффект "короткого прыжка": если отпустили кнопку, быстро гасим скорость
            verticalVelocity.y -= jumpCancelRate * Time.deltaTime;
        }
    }

    private void OnLand()
    {
        // Эффект приземления
        ApplySquashStretch(squashAmount, stretchAmount); // Сплющиваем при посадке

        // Тряска камеры, если упали быстро
        if (lastYVelocity < fallShakeThreshold)
        {
            impulseSource.GenerateImpulse(Mathf.Abs(lastYVelocity) * 0.1f);
            // Можно добавить звук тяжелого удара
        }
    }

    private void ApplySquashStretch(float scaleY, float scaleXZ)
    {
        if (visualRoot == null) return;

        // Сбрасываем текущую анимацию, чтобы не было конфликтов
        visualRoot.DOKill();
        visualRoot.localScale = Vector3.one;

        // Плавная деформация
        visualRoot.DOScale(new Vector3(scaleXZ, scaleY, scaleXZ), juiceDuration).SetLoops(2, LoopType.Yoyo);
    }

    private void HandleHover()
    {
        bool hoverInput = inputActions.Player.Hover.IsPressed();

        if (hoverInput && !isGrounded && currentStamina > 0)
        {
            hoverTimer += Time.deltaTime;
            if (hoverTimer >= hoverHoldThreshold && !isHovering)
            {
                isHovering = true;
                verticalVelocity.y = 0; // "Подхват" в воздухе
                visualRoot.DOPunchRotation(new Vector3(10, 0, 0), 0.3f);
            }
        }
        else
        {
            isHovering = false;
            hoverTimer = 0;
        }

        if (isHovering)
        {
            currentStamina -= staminaDepleteRate * Time.deltaTime;
            if (currentStamina <= 0) isHovering = false;
        }
    }

    private void HandleZoom()
    {
        Vector2 scrollValue = inputActions.Player.Zoom.ReadValue<Vector2>();
        if (Mathf.Abs(scrollValue.y) > 0.1f)
        {
            targetZoomRadius -= Mathf.Sign(scrollValue.y) * zoomSensitivity;
            targetZoomRadius = Mathf.Clamp(targetZoomRadius, minZoomRadius, maxZoomRadius);
        }
        orbitalFollow.Radius = Mathf.Lerp(orbitalFollow.Radius, targetZoomRadius, Time.deltaTime * 5f);
    }

    private void ApplyGravity()
    {
        lastYVelocity = verticalVelocity.y; // Запоминаем скорость перед применением гравитации
        float currentGrav = isHovering ? hoverGravity : gravity;
        verticalVelocity.y += currentGrav * Time.deltaTime;
        controller.Move(verticalVelocity * Time.deltaTime);
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