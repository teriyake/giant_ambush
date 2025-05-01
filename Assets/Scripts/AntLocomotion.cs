using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

public class AntLocomotion : MonoBehaviour
{
    [Header("Hand Controllers")]
    [SerializeField]
    private Transform leftHandController;

    [SerializeField]
    private Transform rightHandController;

    [Header("Input Actions")]
    [SerializeField]
    private InputActionReference leftGripAction;

    [SerializeField]
    private InputActionReference rightGripAction;

    [Header("Movement Settings")]
    [SerializeField]
    private float sensitivity = 1.0f;

    [Header("Climbing Settings")]
    [SerializeField]
    private bool enableVerticalRedirect = true;

    [SerializeField]
    private float climbingSpeedMultiplier = 1.0f;

    [SerializeField]
    private float minHorizontalMoveForClimb = 0.1f;

    [Header("Gravity Settings")]
    [SerializeField]
    private float gravityValue = -9.81f;

    [SerializeField]
    private float groundingForce = -1.0f;

    private CharacterController characterController;
    private XROrigin xrOrigin;

    private bool isGrabbingLeft = false;
    private bool isGrabbingRight = false;
    private Vector3 grabStartPositionLeft_World;
    private Vector3 grabStartPositionRight_World;
    private Vector3 playerStartPositionOnGrabLeft_World;
    private Vector3 playerStartPositionOnGrabRight_World;

    private CollisionFlags lastCollisionFlags;
    private float verticalVelocity = 0f;

    void Awake()
    {
        characterController = GetComponent<CharacterController>();
        xrOrigin = GetComponent<XROrigin>();

        if (characterController == null)
        {
            Debug.LogError(
                "AntLocomotion requires a CharacterController component on the same GameObject.",
                this
            );
            enabled = false;
            return;
        }
        if (leftHandController == null || rightHandController == null)
        {
            Debug.LogError("Please assign Left and Right Hand Controller Transforms.", this);
            enabled = false;
            return;
        }
        if (leftGripAction == null || rightGripAction == null)
        {
            Debug.LogError("Please assign Left and Right Grip Input Action References.", this);
            enabled = false;
            return;
        }

        leftGripAction.action.started += OnGripStartedLeft;
        leftGripAction.action.canceled += OnGripCanceledLeft;

        rightGripAction.action.started += OnGripStartedRight;
        rightGripAction.action.canceled += OnGripCanceledRight;
    }

    void OnEnable()
    {
        leftGripAction.action.Enable();
        rightGripAction.action.Enable();
        verticalVelocity = 0f;
    }

    void OnDisable()
    {
        leftGripAction.action.started -= OnGripStartedLeft;
        leftGripAction.action.canceled -= OnGripCanceledLeft;
        rightGripAction.action.started -= OnGripStartedRight;
        rightGripAction.action.canceled -= OnGripCanceledRight;

        // leftGripAction.action.Disable();
        // rightGripAction.action.Disable();
    }

    void Update()
    {
        bool isGrounded = characterController.isGrounded;

        if (isGrounded && verticalVelocity < 0)
        {
            verticalVelocity = groundingForce;
        }
        else
        {
            verticalVelocity += gravityValue * Time.deltaTime;
        }

        if (!isGrabbingLeft && !isGrabbingRight)
        {
            return;
        }

        Vector3 movementDelta = Vector3.zero;
        if (isGrabbingLeft || isGrabbingRight)
        {
            movementDelta = CalculateMovement();
        }
        Vector3 combinedMoveVector = movementDelta * sensitivity;
        combinedMoveVector.y += verticalVelocity * Time.deltaTime;

        if (characterController != null)
        {
            if (combinedMoveVector != Vector3.zero || !isGrounded)
            {
                lastCollisionFlags = characterController.Move(combinedMoveVector);

                if (!isGrounded && characterController.isGrounded && verticalVelocity < 0)
                {
                    verticalVelocity = groundingForce;
                }
            }
            else
            {
                lastCollisionFlags = characterController.collisionFlags;
            }

            if (
                enableVerticalRedirect
                && (lastCollisionFlags & CollisionFlags.Sides) != 0
                && (isGrabbingLeft || isGrabbingRight)
            )
            {
                Vector3 horizontalIntention = new Vector3(movementDelta.x, 0f, movementDelta.z);
                float horizontalMagnitudePerFrame = horizontalIntention.magnitude * sensitivity;

                if (horizontalMagnitudePerFrame / Time.deltaTime >= minHorizontalMoveForClimb)
                {
                    float climbDisplacement = horizontalMagnitudePerFrame * climbingSpeedMultiplier;

                    CollisionFlags climbFlags = characterController.Move(
                        Vector3.up * climbDisplacement
                    );

                    if ((climbFlags & CollisionFlags.Above) == 0 && climbDisplacement > 0)
                    {
                        verticalVelocity = Mathf.Max(verticalVelocity, 0f);
                    }

                    lastCollisionFlags |= (
                        climbFlags & (CollisionFlags.Above | CollisionFlags.Below)
                    );
                }
            }

            if (isGrabbingLeft)
            {
                Vector3 handDeltaLeft_World =
                    leftHandController.position - grabStartPositionLeft_World;
                Vector3 intendedPlayerTargetLeft =
                    playerStartPositionOnGrabLeft_World - handDeltaLeft_World;
                Vector3 correction = transform.position - intendedPlayerTargetLeft;
                playerStartPositionOnGrabLeft_World += correction;
            }
            if (isGrabbingRight)
            {
                Vector3 handDeltaRight_World =
                    rightHandController.position - grabStartPositionRight_World;
                Vector3 intendedPlayerTargetRight =
                    playerStartPositionOnGrabRight_World - handDeltaRight_World;
                Vector3 correction = transform.position - intendedPlayerTargetRight;
                playerStartPositionOnGrabRight_World += correction;
            }
        }
    }

    private Vector3 CalculateMovement()
    {
        Vector3 currentXROriginPos = transform.position;
        Vector3 combinedTargetPlayerPosition = currentXROriginPos;

        int activeGrabs = 0;
        Vector3 cumulativeTarget = Vector3.zero;

        if (isGrabbingLeft)
        {
            Vector3 handDeltaLeft_World = leftHandController.position - grabStartPositionLeft_World;

            Vector3 targetPlayerPositionLeft =
                playerStartPositionOnGrabLeft_World - handDeltaLeft_World;

            cumulativeTarget += targetPlayerPositionLeft;
            activeGrabs++;
        }

        if (isGrabbingRight)
        {
            Vector3 handDeltaRight_World =
                rightHandController.position - grabStartPositionRight_World;
            Vector3 targetPlayerPositionRight =
                playerStartPositionOnGrabRight_World - handDeltaRight_World;

            cumulativeTarget += targetPlayerPositionRight;
            activeGrabs++;
        }

        if (activeGrabs > 0)
        {
            combinedTargetPlayerPosition = cumulativeTarget / activeGrabs;

            Vector3 movementDelta = combinedTargetPlayerPosition - currentXROriginPos;
            return movementDelta;
        }

        return Vector3.zero;
    }

    private void OnGripStartedLeft(InputAction.CallbackContext context)
    {
        if (!isGrabbingLeft)
        {
            isGrabbingLeft = true;
            grabStartPositionLeft_World = leftHandController.position;
            playerStartPositionOnGrabLeft_World = transform.position;
            // Debug.Log("Left Grip Started");
        }
    }

    private void OnGripCanceledLeft(InputAction.CallbackContext context)
    {
        if (isGrabbingLeft)
        {
            isGrabbingLeft = false;
            // Debug.Log("Left Grip Canceled");
        }
    }

    private void OnGripStartedRight(InputAction.CallbackContext context)
    {
        if (!isGrabbingRight)
        {
            isGrabbingRight = true;
            grabStartPositionRight_World = rightHandController.position;
            playerStartPositionOnGrabRight_World = transform.position;
            // Debug.Log("Right Grip Started");
        }
    }

    private void OnGripCanceledRight(InputAction.CallbackContext context)
    {
        if (isGrabbingRight)
        {
            isGrabbingRight = false;
            // Debug.Log("Right Grip Canceled");
        }
    }
}