
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class improvedHitbox : UdonSharpBehaviour
{
    public bool leg;
    public bool torso;
    public bool head;
    private Transform col;
    [HideInInspector] public VRCPlayerApi assignedPlayer;
    public Transform thingy;
   
    private void Start()
    {
        col = transform;
    }
    //positional tracking based on what it is
    private void PostLateUpdate()
    {
        if(assignedPlayer.IsValid())
        {
            if (leg)
            {
                col.position = (assignedPlayer.GetBonePosition(HumanBodyBones.LeftFoot) + assignedPlayer.GetBonePosition(HumanBodyBones.RightFoot)) / 2;
                //col.rotation = assignedPlayer.GetRotation();
                thingy.SetPositionAndRotation(assignedPlayer.GetBonePosition(HumanBodyBones.Hips), assignedPlayer.GetBoneRotation(HumanBodyBones.Hips));
                //col.LookAt(thingy);
                float distance = Vector3.Distance(col.position, assignedPlayer.GetBonePosition(HumanBodyBones.Hips)) / 2;
                col.localScale = new Vector3(1.0f, 1.0f, distance);
            }
            if (torso)
            {
                col.position = assignedPlayer.GetBonePosition(HumanBodyBones.Hips);
                //col.rotation = assignedPlayer.GetBoneRotation(HumanBodyBones.Hips);
                //getBoneTransform isn't available with udon, so I guess I'll just fucking make one with a position and rotation because apparently both of those are exposed to udon BUT NOT FUCKING getBoneTransform            
                thingy.SetPositionAndRotation(assignedPlayer.GetBonePosition(HumanBodyBones.Neck), assignedPlayer.GetRotation());
                //lookat constraint is dumb
                //col.LookAt(thingy);
               

                //fuck it, why not scale width too
                col.localScale = new Vector3(1.0f, 1.0f, Vector3.Distance(assignedPlayer.GetBonePosition(HumanBodyBones.Hips), assignedPlayer.GetBonePosition(HumanBodyBones.Neck)));
            }
            if (head)
            {
                col.SetPositionAndRotation(assignedPlayer.GetBonePosition(HumanBodyBones.Head), assignedPlayer.GetBoneRotation(HumanBodyBones.Head));
            }
        }
       
    }
}
