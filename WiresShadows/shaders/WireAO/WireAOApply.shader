// Amplify Occlusion 2 - Robust Ambient Occlusion for Unity
// Copyright (c) Amplify Creations, Lda <info@amplify.pt> (MIT, see LICENSE_AmplifyOcclusion.txt)
//
// Wire's Shadows: Amplify's apply shader, the same passes in the same order, swapped in for
// the game's. Only change: in the deferred passes the occlusion is coloured by the surface it
// lands on (GTAO multi-bounce: Jimenez et al. 2016, "Practical Real-Time Strategies for
// Accurate Indirect Occlusion"). Light bounces around inside a bright corner, so a bright
// surface is darkened less, a dark one about as much as before, and a coloured one keeps its
// hue instead of going grey. The surface colour comes from a copy of the G-buffer's albedo the
// mod makes just before (_WireAOAlbedo), or Amplify's own copy on the LDR path.

Shader "Hidden/WireAOApply"
{
	CGINCLUDE
		#pragma vertex vert
		#pragma fragment frag
		#pragma target 3.0
		#pragma exclude_renderers gles d3d11_9x n3ds
		#pragma multi_compile_instancing

		#include "Common.cginc"
		#include "TemporalFilter.cginc"

		UNITY_DECLARE_SCREENSPACE_TEXTURE( _AO_GBufferAlbedo );
		UNITY_DECLARE_SCREENSPACE_TEXTURE( _AO_GBufferEmission );
		UNITY_DECLARE_SCREENSPACE_TEXTURE( _AO_ApplyOcclusionTexture );
		UNITY_DECLARE_SCREENSPACE_TEXTURE( _WireAOAlbedo );

		// The fitted curve from the paper: visibility after the light's extra bounces, per
		// colour channel, from the plain visibility v and the surface's albedo.
		half3 WireBounce( half3 v, half3 albedo )
		{
			const half3 a = 2.0404 * albedo - 0.3324;
			const half3 b = -4.7951 * albedo + 0.6417;
			const half3 c = 2.7552 * albedo + 0.6903;
			return max( v, ( ( v * a + b ) * v + c ) * v );
		}

		half3 WireSurface( const float2 screenPos, const bool log )
		{
			const float2 uv = UnityStereoTransformScreenSpaceTex( screenPos );
			return log ? UNITY_SAMPLE_SCREENSPACE_TEXTURE( _AO_GBufferAlbedo, uv ).rgb
			           : UNITY_SAMPLE_SCREENSPACE_TEXTURE( _WireAOAlbedo, uv ).rgb;
		}


		struct DeferredOutput
		{
			half4 albedo : SV_Target0;
			half4 emission : SV_Target1;
		};

		struct DeferredOutputTemporal
		{
			half4 albedo : SV_Target0;
			half4 emission : SV_Target1;
			half4 temporalAcc : SV_Target2;
		};


		#include "ApplyPostEffect.cginc"


		PostEffectOutputTemporal ApplyPostEffectTemporal( v2f_in IN, const bool aUseMotionVectors )
		{
			UNITY_SETUP_INSTANCE_ID( IN );
			UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( IN );

			const float2 screenPos = IN.uv.xy;

			const half2 occlusionDepth = FetchOcclusionDepth( screenPos );

			PostEffectOutputTemporal OUT;

			if( occlusionDepth.y < HALF_MAX )
			{
				const half linear01Depth = occlusionDepth.y * ONE_OVER_DEPTH_SCALE;
				const half sampledDepth = Linear01ToSampledDepth( linear01Depth );
				const half linearEyeDepth = occlusionDepth.y * _AO_BufDepthToLinearEye;

				half occlusion;
				const half4 temporalAcc = TemporalFilter( screenPos, occlusionDepth.x, sampledDepth, linearEyeDepth, linear01Depth, aUseMotionVectors, occlusion );

				const half4 occlusionRGBA = CalcOcclusion( occlusion, linearEyeDepth );

				OUT.occlusionColor = occlusionRGBA;
				OUT.temporalAcc = temporalAcc;
			}
			else
			{
				OUT.occlusionColor = half4( (1).xxxx );
				OUT.temporalAcc = half4( (1).xxxx );
			}

			return OUT;
		}


		half4 ApplyPostEffect( v2f_in IN )
		{
			UNITY_SETUP_INSTANCE_ID( IN );
			UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( IN );

			const half2 screenPos = IN.uv.xy;

			const half2 occlusionDepth = FetchOcclusionDepth( screenPos );

			const half linearEyeDepth = occlusionDepth.y * _AO_BufDepthToLinearEye;

			const half4 occlusionRGBA = CalcOcclusion( occlusionDepth.x, linearEyeDepth );

			return half4( occlusionRGBA.rgb, 1 );
		}


		DeferredOutput ApplyDeferred( v2f_in IN, const bool log )
		{
			UNITY_SETUP_INSTANCE_ID( IN );
			UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( IN );

			const half2 screenPos = IN.uv.xy;

			const half2 occlusionDepth = FetchOcclusionDepth( screenPos );

			const half linearEyeDepth = occlusionDepth.y * _AO_BufDepthToLinearEye;

			half4 occlusionRGBA = CalcOcclusion( occlusionDepth.x, linearEyeDepth );
			occlusionRGBA.rgb = WireBounce( occlusionRGBA.rgb, WireSurface( screenPos, log ) );

			half4 emission, albedo;

			if ( log )
			{
				emission = UNITY_SAMPLE_SCREENSPACE_TEXTURE( _AO_GBufferEmission, UnityStereoTransformScreenSpaceTex( screenPos ) );
				albedo = UNITY_SAMPLE_SCREENSPACE_TEXTURE( _AO_GBufferAlbedo, UnityStereoTransformScreenSpaceTex( screenPos ) );

				emission.rgb = -log2( emission.rgb );
				emission.rgb *= occlusionRGBA.rgb;

				albedo.a *= occlusionRGBA.a;

				emission.rgb = exp2( -emission.rgb );
			}
			else
			{
				albedo = half4( 1, 1, 1, occlusionRGBA.a );
				emission = half4( occlusionRGBA.rgb, 1 );
			}

			DeferredOutput OUT;
			OUT.albedo = albedo;
			OUT.emission = emission;
			return OUT;
		}


		DeferredOutput ApplyDeferredCombineFromTemporal( v2f_in IN, const bool log )
		{
			UNITY_SETUP_INSTANCE_ID( IN );
			UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( IN );

			const half2 screenPos = IN.uv.xy;

			const half depthSample = SampleDepth0( screenPos );

			const half2 occlusionLinearEyeDepth = ComputeCombineDownsampledOcclusionFromTemporal( screenPos, depthSample );

			half4 occlusionRGBA = CalcOcclusion( occlusionLinearEyeDepth.x, occlusionLinearEyeDepth.y );
			occlusionRGBA.rgb = WireBounce( occlusionRGBA.rgb, WireSurface( screenPos, log ) );

			half4 emission, albedo;

			if ( log )
			{
				emission = UNITY_SAMPLE_SCREENSPACE_TEXTURE( _AO_GBufferEmission, UnityStereoTransformScreenSpaceTex( screenPos ) );
				albedo = UNITY_SAMPLE_SCREENSPACE_TEXTURE( _AO_GBufferAlbedo, UnityStereoTransformScreenSpaceTex( screenPos ) );

				emission.rgb = -log2( emission.rgb );
				emission.rgb *= occlusionRGBA.rgb;

				albedo.a *= occlusionRGBA.a;

				emission.rgb = exp2( -emission.rgb );
			}
			else
			{
				albedo = half4( 1, 1, 1, occlusionRGBA.a );
				emission = half4( occlusionRGBA.rgb, 1 );
			}

			DeferredOutput OUT;
			OUT.albedo = albedo;
			OUT.emission = emission;
			return OUT;
		}


		DeferredOutputTemporal ApplyDeferredTemporal( v2f_in IN, const bool log, const bool aUseMotionVectors )
		{
			UNITY_SETUP_INSTANCE_ID( IN );
			UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( IN );

			const float2 screenPos = IN.uv.xy;

			const half2 occlusionDepth = FetchOcclusionDepth( screenPos );

			DeferredOutputTemporal OUT;

			if( occlusionDepth.y < HALF_MAX )
			{
				const half linear01Depth = occlusionDepth.y * ONE_OVER_DEPTH_SCALE;
				const half sampledDepth = Linear01ToSampledDepth( linear01Depth );
				const half linearEyeDepth = occlusionDepth.y * _AO_BufDepthToLinearEye;

				half occlusion;
				const half4 temporalAcc = TemporalFilter( screenPos, occlusionDepth.x, sampledDepth, linearEyeDepth, linear01Depth, aUseMotionVectors, occlusion );

				half4 occlusionRGBA = CalcOcclusion( occlusion, linearEyeDepth );
				occlusionRGBA.rgb = WireBounce( occlusionRGBA.rgb, WireSurface( screenPos, log ) );

				half4 emission, albedo;

				if ( log )
				{
					emission = UNITY_SAMPLE_SCREENSPACE_TEXTURE( _AO_GBufferEmission, UnityStereoTransformScreenSpaceTex( screenPos ) );
					albedo = UNITY_SAMPLE_SCREENSPACE_TEXTURE( _AO_GBufferAlbedo, UnityStereoTransformScreenSpaceTex( screenPos ) );

					emission.rgb = -log2( emission.rgb );
					emission.rgb *= occlusionRGBA.rgb;

					albedo.a *= occlusionRGBA.a;

					emission.rgb = exp2( -emission.rgb );
				}
				else
				{
					albedo = half4( 1, 1, 1, occlusionRGBA.a );
					emission = half4( occlusionRGBA.rgb, 1 );
				}

				OUT.albedo = albedo;
				OUT.emission = emission;
				OUT.temporalAcc = temporalAcc;
			}
			else
			{
				half4 emission, albedo;

				if ( log )
				{
					emission = UNITY_SAMPLE_SCREENSPACE_TEXTURE( _AO_GBufferEmission, UnityStereoTransformScreenSpaceTex( screenPos ) );
					albedo = UNITY_SAMPLE_SCREENSPACE_TEXTURE( _AO_GBufferAlbedo, UnityStereoTransformScreenSpaceTex( screenPos ) );
				}
				else
				{
					albedo = half4( (1).xxxx );
					emission = half4( (1).xxxx );
				}

				OUT.albedo = albedo;
				OUT.emission = emission;
				OUT.temporalAcc = half4( (1).xxxx );
			}

			return OUT;
		}


		half4 ApplyPostEffectTemporalMultiply( v2f_in IN )
		{
			UNITY_SETUP_INSTANCE_ID( IN );
			UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( IN );

			return UNITY_SAMPLE_SCREENSPACE_TEXTURE( _AO_ApplyOcclusionTexture, UnityStereoTransformScreenSpaceTex( IN.uv.xy ) );
		}

		DeferredOutput ApplyDeferredTemporalMultiply( v2f_in IN )
		{
			UNITY_SETUP_INSTANCE_ID( IN );
			UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( IN );

			half4 occlusionRGBA = UNITY_SAMPLE_SCREENSPACE_TEXTURE( _AO_ApplyOcclusionTexture, UnityStereoTransformScreenSpaceTex( IN.uv.xy ) );
			occlusionRGBA.rgb = WireBounce( occlusionRGBA.rgb, WireSurface( IN.uv.xy, false ) );

			DeferredOutput OUT;
			OUT.albedo = half4( (1.0).xxx, occlusionRGBA.a );
			OUT.emission = half4( occlusionRGBA.rgb, 1.0 );

			return OUT;
		}

	ENDCG

	///////////////////////////////////////////////////////////////////////////////////////
	// MRT BLENDING PATH
	///////////////////////////////////////////////////////////////////////////////////////

	SubShader
	{
		Tags { "MRTBlending" = "True" }
		ZTest Always Cull Off ZWrite Off

		// -- APPLICATION METHODS --------------------------------------------------------------
		// 0 => APPLY DEBUG
		Pass { CGPROGRAM half4 frag( v2f_in IN ) : SV_Target { return ApplyDebug( IN ); } ENDCG }
		// 1 => APPLY DEBUG Temporal
		Pass { CGPROGRAM PostEffectOutputTemporal frag( v2f_in IN ) { return ApplyDebugTemporal( IN, false); } ENDCG }
		Pass { CGPROGRAM PostEffectOutputTemporal frag( v2f_in IN ) { return ApplyDebugTemporal( IN, true ); } ENDCG }

		// 3 => APPLY DEFERRED
		Pass
		{
			Blend DstColor Zero, DstAlpha Zero
			CGPROGRAM DeferredOutput frag( v2f_in IN ) { return ApplyDeferred( IN, false ); } ENDCG
		}
		// 4 => APPLY DEFERRED Temporal
		Pass
		{
			Blend 0 DstColor Zero, DstAlpha Zero
			Blend 1 DstColor Zero, DstAlpha Zero
			Blend 2 Off
			CGPROGRAM DeferredOutputTemporal frag( v2f_in IN ) { return ApplyDeferredTemporal( IN, false, false ); } ENDCG
		}
		Pass
		{
			Blend 0 DstColor Zero, DstAlpha Zero
			Blend 1 DstColor Zero, DstAlpha Zero
			Blend 2 Off
			CGPROGRAM DeferredOutputTemporal frag( v2f_in IN ) { return ApplyDeferredTemporal( IN, false, true ); } ENDCG
		}

		// 6 => APPLY DEFERRED (LOG)
		Pass { CGPROGRAM DeferredOutput frag( v2f_in IN ) { return ApplyDeferred( IN, true ); } ENDCG }
		// 7 => APPLY DEFERRED (LOG) Temporal
		Pass { CGPROGRAM DeferredOutputTemporal frag( v2f_in IN ) { return ApplyDeferredTemporal( IN, true, false ); } ENDCG }
		Pass { CGPROGRAM DeferredOutputTemporal frag( v2f_in IN ) { return ApplyDeferredTemporal( IN, true, true ); } ENDCG }


		// 9 => APPLY POST-EFFECT
		Pass
		{
			Blend DstColor Zero
			CGPROGRAM half4 frag( v2f_in IN ) : SV_Target { return ApplyPostEffect( IN ); } ENDCG
		}
		// 10 => APPLY POST-EFFECT Temporal
		Pass
		{
			Blend 0 DstColor Zero
			Blend 1 Off
			CGPROGRAM PostEffectOutputTemporal frag( v2f_in IN ) { return ApplyPostEffectTemporal( IN, false ); } ENDCG
		}
		Pass
		{
			Blend 0 DstColor Zero
			Blend 1 Off
			CGPROGRAM PostEffectOutputTemporal frag( v2f_in IN ) { return ApplyPostEffectTemporal( IN, true ); } ENDCG
		}

		// 12 => dummy
		Pass { }

		// 13 => dummy
		Pass { }

		// 14 => APPLY DEBUG Combine from Temporal
		Pass { CGPROGRAM half4 frag( v2f_in IN ) : SV_Target { return ApplyDebugCombineFromTemporal( IN ); } ENDCG }

		// 15 => APPLY POST-EFFECT Combine from Temporal
		Pass
		{
			Blend DstColor Zero
			CGPROGRAM half4 frag( v2f_in IN ) : SV_Target { return ApplyCombineFromTemporal( IN ); } ENDCG
		}

		// 16 => APPLY DEFERRED Combine from Temporal
		Pass
		{
			Blend DstColor Zero, DstAlpha Zero
			CGPROGRAM DeferredOutput frag( v2f_in IN ) { return ApplyDeferredCombineFromTemporal( IN, false ); } ENDCG
		}

		// 17 => APPLY DEFERRED (LOG) Combine from Temporal
		Pass { CGPROGRAM DeferredOutput frag( v2f_in IN ) { return ApplyDeferredCombineFromTemporal( IN, true ); } ENDCG }
	}

	///////////////////////////////////////////////////////////////////////////////////////
	// NO MRT BLENDING FALLBACK
	///////////////////////////////////////////////////////////////////////////////////////

	SubShader
	{
		Tags { "MRTBlending" = "False" }
		ZTest Always Cull Off ZWrite Off

		// -- APPLICATION METHODS --------------------------------------------------------------
		// 0 => APPLY DEBUG
		Pass { CGPROGRAM half4 frag( v2f_in IN ) : SV_Target { return ApplyDebug( IN ); } ENDCG }
		// 1 => APPLY DEBUG Temporal
		Pass { CGPROGRAM PostEffectOutputTemporal frag( v2f_in IN ) { return ApplyDebugTemporal( IN, false ); } ENDCG }
		Pass { CGPROGRAM PostEffectOutputTemporal frag( v2f_in IN ) { return ApplyDebugTemporal( IN, true ); } ENDCG }

		// 3 => APPLY DEFERRED
		Pass
		{
			Blend DstColor Zero, DstAlpha Zero
			CGPROGRAM DeferredOutput frag( v2f_in IN ) { return ApplyDeferred( IN, false ); } ENDCG
		}
		// 4 => APPLY DEFERRED Temporal
		Pass { CGPROGRAM DeferredOutputTemporal frag( v2f_in IN ) { return ApplyDeferredTemporal( IN, false, false ); } ENDCG }
		Pass { CGPROGRAM DeferredOutputTemporal frag( v2f_in IN ) { return ApplyDeferredTemporal( IN, false, true ); } ENDCG }

		// 6 => APPLY DEFERRED (LOG)
		Pass { CGPROGRAM DeferredOutput frag( v2f_in IN ) { return ApplyDeferred( IN, true ); } ENDCG }
		// 7 => APPLY DEFERRED (LOG) Temporal
		Pass { CGPROGRAM DeferredOutputTemporal frag( v2f_in IN ) { return ApplyDeferredTemporal( IN, true, false ); } ENDCG }
		Pass { CGPROGRAM DeferredOutputTemporal frag( v2f_in IN ) { return ApplyDeferredTemporal( IN, true, true ); } ENDCG }

		// 9 => APPLY POST-EFFECT
		Pass { Blend DstColor Zero CGPROGRAM half4 frag( v2f_in IN ) : SV_Target { return ApplyPostEffect( IN ); } ENDCG }
		// 10 => APPLY POST-EFFECT Temporal
		Pass { CGPROGRAM PostEffectOutputTemporal frag( v2f_in IN ) { return ApplyPostEffectTemporal( IN, false ); } ENDCG }
		Pass { CGPROGRAM PostEffectOutputTemporal frag( v2f_in IN ) { return ApplyPostEffectTemporal( IN, true ); } ENDCG }

		// 12 => APPLY POST-EFFECT Temporal Multiply
		Pass
		{
			Blend DstColor Zero
			CGPROGRAM half4 frag( v2f_in IN ) : SV_Target0 { return ApplyPostEffectTemporalMultiply( IN ); } ENDCG
		}

		// 13 => APPLY DEFERRED Temporal Multiply
		Pass
		{
			Blend DstColor Zero, DstAlpha Zero
			CGPROGRAM DeferredOutput frag( v2f_in IN ) { return ApplyDeferredTemporalMultiply( IN ); } ENDCG
		}

		// 14 => APPLY DEBUG Combine from Temporal
		Pass { CGPROGRAM half4 frag( v2f_in IN ) : SV_Target { return ApplyDebugCombineFromTemporal( IN ); } ENDCG }

		// 15 => APPLY POST-EFFECT Combine from Temporal
		Pass
		{
			Blend DstColor Zero
			CGPROGRAM half4 frag( v2f_in IN ) : SV_Target { return ApplyCombineFromTemporal( IN ); } ENDCG
		}

		// 16 => APPLY DEFERRED Combine from Temporal
		Pass
		{
			Blend DstColor Zero, DstAlpha Zero
			CGPROGRAM DeferredOutput frag( v2f_in IN ) { return ApplyDeferredCombineFromTemporal( IN, false ); } ENDCG
		}

		// 17 => APPLY DEFERRED (LOG) Combine from Temporal
		Pass { CGPROGRAM DeferredOutput frag( v2f_in IN ) { return ApplyDeferredCombineFromTemporal( IN, true ); } ENDCG }
	}

	Fallback Off
}
