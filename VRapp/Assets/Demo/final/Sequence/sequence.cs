using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

public class sequence : MonoBehaviourPunCallbacks, IPunObservable {

    // Controller legend
    // A = 3
    // B = 0
    // C = 2
    // D = 1
    // L = 4
    // R = 5
    // POWER = 10
    // @ = nothing
    // Joystick = nothing

    // Mode legend
    // 0 = joystick, post = off
    // 1 = joystick, post = on
    // 2 = arm, post = off
    // 3 = arm, post = on
    // 4 = head, post = off
    // 5 = head, post = on

    // constants
    Vector3 origin1 = new Vector3(205.4f, 12.1f, -6817.8f);
    Vector3 origin2 = new Vector3(-27.55f, -3.5f, -40.65f);
    Vector3 currentOrigin = new Vector3();
   
    // Variables for move
    float horizontal, vertical;
    public Camera Cam;
    public GameObject red;
    public GameObject white;
    public RectTransform whiteRect;
    public RectTransform redRect;

    // Variables for jump
    public Rigidbody Cube;
    public float jumpForce = 2.1f;
    public Vector3 jump;
    public bool isGrounded;
    public bool isDragging;
    public Vector3 currentDestination;
    float oldTilt;
    float smoothing;

    int mode;
    int lastMode;

    public GameObject volHost;
    public PostProcessVolume vol;
    public Vignette vign;


    void OnCollisionStay()
        {
            isGrounded = true;
        }
        
    void OnCollisionEnter(Collision collision)
        {
            if (collision.gameObject.name == "trigger"){
                currentOrigin = origin2;
                Cube.transform.position = currentOrigin;
            }

            if (collision.gameObject.name == "trigger2"){
                currentOrigin = origin1;
                Cube.transform.position = currentOrigin;
            }
        }

    public Vector3 getCentre(){

        float xCentre = Cam.pixelWidth/2.0f;
        float yCentre = Cam.pixelHeight/2.0f;
        Vector3 centreCoords = new Vector3(xCentre, yCentre, 0.0f);

        return centreCoords;
    }

    public float getDistance2d(Vector3 p1, Vector3 p2){
        float y2 = p2.z;
        float x2 = p2.x;
        float y1 = p1.z;
        float x1 = p1.x;

        float dist = Mathf.Sqrt((x2-x1)*(x2-x1)+(y2-y1)*(y2-y1));
        return dist;
    }

    // Start is called before the first frame update
    private void Start() {

        currentOrigin = origin2;
        Cube.transform.position = currentOrigin;

        mode = 0;
        lastMode = 0;

        jump = new Vector3(0.0f, 2.1f, 0.0f);

        isDragging = false;

        whiteRect = white.GetComponent<RectTransform>();
        redRect = red.GetComponent<RectTransform>();

        oldTilt = 0.0f;
        smoothing = 0.1f;

        PhotonNetwork.ConnectUsingSettings();
        PhotonNetwork.SendRate = 60;
        PhotonNetwork.SerializationRate = 60;
    }

    public override void OnConnectedToMaster()
    {
        Debug.Log("Connected to Master");
        RoomOptions roomOptions = new RoomOptions();
        roomOptions.MaxPlayers = 2;
        PhotonNetwork.JoinOrCreateRoom("Retea", roomOptions, TypedLobby.Default);
    }

    public override void OnJoinedRoom() {
        if (PhotonNetwork.IsMasterClient) {
            Debug.LogWarning("We are the MasterClient");
        } else {
            Debug.LogWarning("Not the MasterClient");
        }  
    }

    public override void OnJoinRoomFailed(short returnCode, string message) {
        Debug.LogError("OnJoinRoomFailed - " + message);
    }

    public override void OnDisconnected(DisconnectCause cause) {
        Debug.LogErrorFormat("Disconnected - {0}", cause);
    }

    public override void OnPlayerEnteredRoom(Player newPlayer) {
        Debug.Log("Player joined - " + newPlayer);
    }

    public override void OnPlayerLeftRoom(Player otherPlayer) {
        Debug.LogError("Player left - " + otherPlayer);
    }


    float currentAcceleration; // this is sent by the master device
    float receivedAcceleration; // this is used by the recipient device

    float minVign = 0.42f;
    float maxVignJoy = 0.84f;
    float maxVignReal = 0.81f;
    float offsetVignJoy = 0.003f;
    float offsetVignReal = 0.013f;


    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info){
        if (stream.IsWriting) {
            stream.SendNext(currentAcceleration);
        } else {
            receivedAcceleration = (float) stream.ReceiveNext();
        }
    }

    public Vector3 getDot(){
        Vector3 screenPoint = Cam.WorldToScreenPoint(white.transform.position);
        screenPoint.z = 0.0f;
        return screenPoint;
    }

    void vignIncrease(){
        float maxVign = maxVignReal;
        if(mode == 1)
            maxVign = maxVignJoy;

        if(vign.intensity.value <= maxVign)
            if(mode == 1)
                vign.intensity.value += offsetVignJoy;
            else
                vign.intensity.value += offsetVignReal;
    }

    void vignDecrease(){
        if(vign.intensity.value > minVign)
            if(mode == 1)
                vign.intensity.value -= offsetVignJoy;
            else
                vign.intensity.value -= offsetVignReal;
    }

    // Update is called once per frame
    void Update() {

        
        if (PhotonNetwork.IsMasterClient) {
            // we only want to compute these on the master device.
            currentAcceleration = Input.acceleration.x;
        }
        else {  // otherwise, doing computations

            // reboot player if fallen off
            if(Cube.transform.position.y < -42f)
                Cube.transform.position = currentOrigin;
            

            ///////// Switching locomotion mode
            lastMode = mode;
            if(Input.GetKeyDown(KeyCode.JoystickButton2)){
                mode += 1;
                if(mode == 4)
                    mode = 0;
            }

            //////// Enabling/disabling volume
            if(lastMode != mode) {
                if(mode == 0 || mode == 2 || mode == 4)
                    volHost.SetActive(false);
                else {
                    volHost.SetActive(true);
                    vol.profile.TryGetSettings(out vign);
                    vign.intensity.value = minVign;
                }
            }


            ////////////////////////////////////////////////////////
            /////////// JOYSTICK CONTROL

            if(mode == 0 || mode == 1){

                // disable crosshair
                red.SetActive(false);
                white.SetActive(false);

                // MOVE
                horizontal = Input.GetAxis("Horizontal");     //joystick horizontal
                vertical = Input.GetAxis("Vertical");      //joystick vertical

                Quaternion rotationVector = Quaternion.Euler(0, Cam.transform.eulerAngles.y, 0);        
                transform.position += rotationVector * new Vector3(vertical * 0.05f, 0, horizontal * -0.05f);

                // BOOST
                if(Input.GetKeyDown(KeyCode.JoystickButton4)){
                    Cube.AddForce(rotationVector * new Vector3(0f, 0f, 19f), ForceMode.Impulse);
                }

                // JUMP
                if((Input.GetKeyDown(KeyCode.JoystickButton5)) && isGrounded){
                    Cube.AddForce(jump * jumpForce, ForceMode.Impulse);
                    isGrounded = false;
                }

                // FOV manager
                if(mode == 1)
                    if(horizontal != 0.0f || vertical != 0.0f || Input.GetKey(KeyCode.JoystickButton4) || Input.GetKey(KeyCode.JoystickButton5))
                        vignIncrease();
                    else
                        vignDecrease();

            } /// END OF JOYSTICK CONTROL


            /////////////////////////////////////////////////////////
            //////////  ARM CONTROL         

            if(mode == 2 || mode == 3){

                // enable crosshair
                white.SetActive(true);

                float tilt = Mathf.Lerp(oldTilt, receivedAcceleration, smoothing); // attempt at smoothing
                oldTilt = tilt;

                Quaternion rotationVector = Quaternion.Euler(0, Cam.transform.eulerAngles.y, 0);   

                /// dot movement
                float screenHeight = 300.0f; 
                float newYCoord = tilt*screenHeight/2;
                whiteRect.anchoredPosition = new Vector2(0.0f, newYCoord);
                redRect.anchoredPosition = new Vector2(0.0f, newYCoord);


                // colouring dot
                Vector3 dotCoords = getDot();

                Ray ray = Cam.ScreenPointToRay(dotCoords);
                RaycastHit hit;

                if(Physics.Raycast(ray, out hit)) {
                    float dist = getDistance2d(transform.position, hit.point);

                    if(dist > 19.0f)
                        red.SetActive(true);
                    else
                        red.SetActive(false);
                }
                else
                    red.SetActive(true);

                // checking if any drag has been initiated
                if(isDragging == false){

                    if(Input.GetKey(KeyCode.JoystickButton4)){

                        // initiate move

                        dotCoords = getDot();
                        ray = Cam.ScreenPointToRay(dotCoords);

                        // RaycastHit hit;
                        if(Physics.Raycast(ray, out hit)) {
                            // Debug.Log("attached");
                            isDragging = true;
                            currentDestination = hit.point;
                        }
                    }
                }

                // doing movement
                if(isDragging == true){

                    // key still needs to be pressed
                    if(Input.GetKey(KeyCode.JoystickButton4)){

                        // movement code
                        dotCoords = getDot();
                        ray = Cam.ScreenPointToRay(dotCoords);
                        
                        // RaycastHit hit;
                        Vector3 currentHit;

                        if(Physics.Raycast(ray, out hit)) {
                            // Debug.Log("getting current hit");

                            currentHit = hit.point;

                            // Debug.Log("performing move");

                            Vector3 diff = currentDestination - currentHit;
                            diff.y = 0.0f;

                            if(red.activeSelf == false)
                                transform.position += diff/5;
                        }
                    }
                    else {
                        isDragging = false;
                    }
                }

                // JUMP
                if((Input.GetKeyDown(KeyCode.JoystickButton5)) && isGrounded){
                    Cube.AddForce(jump * jumpForce, ForceMode.Impulse);
                    isGrounded = false;
                }

                // FOV manager
                if(mode == 3)
                    if(Input.GetKey(KeyCode.JoystickButton4) || Input.GetKey(KeyCode.JoystickButton5))
                        vignIncrease();
                    else
                        vignDecrease();

            } /// END OF ARM CONTROL


            /////////////////////////////////////////////
            ///// HEAD CONTROL
            if(mode == 4 || mode == 5){

                // enable crosshair
                white.SetActive(true);

                // colouring dot
                Vector3 centreCoords = getCentre();

                Ray ray = Cam.ScreenPointToRay(centreCoords);
                RaycastHit hit;

                if(Physics.Raycast(ray, out hit)) {
                    float dist = getDistance2d(transform.position, hit.point);

                    if(dist > 25.0f)
                        red.SetActive(true);
                    else
                        red.SetActive(false);
                }
                else
                    red.SetActive(true);


                // checking if any drag has been pressed
                if(isDragging == false){

                    if(Input.GetKey(KeyCode.JoystickButton4)){

                        // initiate move

                        // Vector3 centreCoords = getCentre();
                        centreCoords = getCentre();


                        // Ray ray = Cam.ScreenPointToRay(centreCoords);
                        ray = Cam.ScreenPointToRay(centreCoords);

                        
                        // RaycastHit hit;

                        if(Physics.Raycast(ray, out hit)) {
                            // Debug.Log("attached");
                            isDragging = true;
                            currentDestination = hit.point;
                        }
                    }
                }

                // doing movement
                if(isDragging == true){

                    // key still needs to be pressed
                    if(Input.GetKey(KeyCode.JoystickButton4)){

                        // movement code
                        centreCoords = getCentre();

                        ray = Cam.ScreenPointToRay(centreCoords);
                        
                        // RaycastHit hit;
                        Vector3 currentHit;

                        if(Physics.Raycast(ray, out hit)) {
                            currentHit = hit.point;

                            Vector3 diff = currentDestination - currentHit;
                            diff.y = 0.0f;

                            if(red.activeSelf == false)
                                transform.position += diff/5;
                        }
                    }
                    else {
                        isDragging = false;
                    }
                }

                if((Input.GetKeyDown(KeyCode.JoystickButton5)) && isGrounded){
                    Cube.AddForce(jump * jumpForce, ForceMode.Impulse);
                    isGrounded = false;
                }

                // FOV manager
                if(mode == 5)
                    if(Input.GetKey(KeyCode.JoystickButton4) || Input.GetKey(KeyCode.JoystickButton5))
                        vignIncrease();
                    else
                        vignDecrease();

            } /// END OF HEAD CONTROL
        }    
    }
}
