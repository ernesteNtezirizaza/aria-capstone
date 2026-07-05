"""
export_to_onnx.py
==================
Exports a trained SB3 PPO checkpoint (MultiInputPolicy + ARIACNNExtractor,
Dict observation space) to ONNX for use in Unity (via Sentis).

REQUIRED PACKAGES (install if missing):
    pip install onnx onnxscript onnxruntime

USAGE:
    python export_to_onnx.py --checkpoint "<path-to-best_model.zip>" --output aria_policy.onnx

"""

import argparse
import os
import sys
import numpy as np
import torch
import torch.nn as nn

PROJECT_ROOT = os.path.dirname(os.path.abspath(__file__))
if PROJECT_ROOT not in sys.path:
    sys.path.insert(0, PROJECT_ROOT)

ZONE_SIZE   = 120
OBS_WINDOW  = 11
N_CHANNELS  = 5
N_ACTIONS   = 47

OBS_SPEC = [
    ("terrain_window",   (OBS_WINDOW, OBS_WINDOW, N_CHANNELS),   torch.float32),
    ("drone_vector",     (10,),                                   torch.float32),
    ("coverage_map",     (ZONE_SIZE, ZONE_SIZE, 1),               torch.float32),
    ("lifecycle_map",    (ZONE_SIZE, ZONE_SIZE, 1),               torch.float32),
    ("disturbance_map",  (ZONE_SIZE, ZONE_SIZE, 1),               torch.float32),
    ("obstacle_map",     (ZONE_SIZE, ZONE_SIZE, 1),               torch.float32),
    ("mission_vector",   (8,),                                    torch.float32),
    ("terrain_stats",    (6,),                                    torch.float32),
]


class PolicyONNXWrapper(nn.Module):
    """
    Wraps the trained SB3 PPO policy's feature extractor + action head
    so it can be traced by torch.onnx.export() with named tensor inputs
    instead of a Gym Dict observation.
    """

    def __init__(self, sb3_policy):
        super().__init__()
        self.features_extractor = sb3_policy.features_extractor
        self.mlp_extractor      = sb3_policy.mlp_extractor
        self.action_net          = sb3_policy.action_net

    def forward(self, terrain_window, drone_vector, coverage_map,
                lifecycle_map, disturbance_map, obstacle_map,
                mission_vector, terrain_stats):
        obs = {
            "terrain_window":   terrain_window,
            "drone_vector":     drone_vector,
            "coverage_map":     coverage_map,
            "lifecycle_map":    lifecycle_map,
            "disturbance_map":  disturbance_map,
            "obstacle_map":     obstacle_map,
            "mission_vector":   mission_vector,
            "terrain_stats":    terrain_stats,
        }
        features = self.features_extractor(obs)
        latent_pi, _latent_vf = self.mlp_extractor(features)
        logits = self.action_net(latent_pi)
        return logits  # shape (batch, N_ACTIONS) -- raw logits, NOT softmax


def build_dummy_inputs(batch_size: int = 1):
    """Random inputs matching observation_space bounds, for tracing + testing."""
    inputs = {}
    for name, shape, dtype in OBS_SPEC:
        full_shape = (batch_size,) + shape
        if name == "lifecycle_map":
            t = torch.empty(full_shape, dtype=dtype).uniform_(-1.0, 1.0)
        else:
            t = torch.empty(full_shape, dtype=dtype).uniform_(0.0, 1.0)
        inputs[name] = t
    return inputs


def export(checkpoint_path: str, output_path: str, opset: int = 17):
    from stable_baselines3 import PPO

    print(f"[1/5] Loading checkpoint: {checkpoint_path}")
    model = PPO.load(checkpoint_path, device="cpu")
    model.policy.eval()
    print(f"      Policy class: {type(model.policy).__name__}")
    print(f"      Features extractor: {type(model.policy.features_extractor).__name__}")
    print(f"      Action space: {model.action_space}")

    wrapper = PolicyONNXWrapper(model.policy)
    wrapper.eval()

    print("[2/5] Building dummy inputs for tracing...")
    dummy = build_dummy_inputs(batch_size=1)
    for k, v in dummy.items():
        print(f"      {k:18s} shape={tuple(v.shape)} dtype={v.dtype}")

    print("[3/5] Tracing & exporting to ONNX...")
    input_names  = [name for name, _, _ in OBS_SPEC]
    output_names = ["action_logits"]
    dynamic_axes = {name: {0: "batch"} for name in input_names}
    dynamic_axes["action_logits"] = {0: "batch"}

    torch.onnx.export(
        wrapper,
        tuple(dummy[name] for name in input_names),
        output_path,
        input_names=input_names,
        output_names=output_names,
        dynamic_axes=dynamic_axes,
        opset_version=opset,
        do_constant_folding=True,
        external_data=False,
    )
    print(f"      Saved: {output_path}")

    print("[4/5] Verifying export with PyTorch reference output...")
    with torch.no_grad():
        torch_out = wrapper(*[dummy[name] for name in input_names]).numpy()

    print("[5/5] Verifying export with ONNX Runtime...")
    try:
        import onnxruntime as ort
    except ImportError:
        print("      onnxruntime not installed -- skipping numerical "
              "verification. Install with: pip install onnxruntime")
        print("      WARNING: export was NOT numerically verified.")
        return

    sess = ort.InferenceSession(output_path, providers=["CPUExecutionProvider"])
    ort_inputs = {name: dummy[name].numpy() for name, _, _ in OBS_SPEC}
    ort_out = sess.run(["action_logits"], ort_inputs)[0]

    max_abs_diff = float(np.max(np.abs(torch_out - ort_out)))
    print(f"      PyTorch  logits sample: {torch_out[0][:5]}")
    print(f"      ONNX     logits sample: {ort_out[0][:5]}")
    print(f"      Max absolute difference: {max_abs_diff:.8f}")

    TOLERANCE = 1e-4
    if max_abs_diff < TOLERANCE:
        print(f"      PASS -- outputs match within tolerance ({TOLERANCE})")
        same_argmax = np.argmax(torch_out, axis=-1) == np.argmax(ort_out, axis=-1)
        print(f"      Argmax (selected action) matches: {bool(same_argmax.all())}")
    else:
        print(f"      FAIL -- difference exceeds tolerance ({TOLERANCE})")
        print(f"      DO NOT USE this ONNX file until this is resolved.")
        sys.exit(1)

    print()
    print("Export verified successfully.")
    print(f"ONNX file: {os.path.abspath(output_path)}")
    print()
    print("Unity-side action decoding reminder (from configs/config.py):")
    print(f"  N_ACTIONS = {N_ACTIONS}")
    print("  In Unity, take argmax(action_logits) across the 47 outputs,")
    print("  then decode it using the SAME action-index mapping defined")
    print("  in rwanda_env.py's step() function (movement+species combos,")
    print("  plus the mission-level actions like abort/return/land).")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--checkpoint", required=True,
                         help="Path to SB3 PPO .zip checkpoint")
    parser.add_argument("--output", default="aria_policy.onnx")
    parser.add_argument("--opset", type=int, default=17)
    args = parser.parse_args()
    export(args.checkpoint, args.output, args.opset)