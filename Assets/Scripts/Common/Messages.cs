using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

public class Messages
{
    public static string GetShutdownReasonMessage(ShutdownReason reason)
    {
        switch (reason)
        {
            case ShutdownReason.Ok:
                return "정상 종료";
            case ShutdownReason.Error:
                return "알 수 없는 오류";
            case ShutdownReason.ServerInRoom:
                return "서버가 이미 다른 방에 있습니다";
            case ShutdownReason.DisconnectedByPluginLogic:
                return "플러그인 로직에 의해 연결 해제";
            case ShutdownReason.GameClosed:
                return "게임이 종료되었습니다";
            case ShutdownReason.GameNotFound:
                return "방을 찾을 수 없습니다";
            case ShutdownReason.MaxCcuReached:
                return "최대 동시 접속자 수 도달";
            case ShutdownReason.InvalidRegion:
                return "잘못된 리전";
            case ShutdownReason.GameIdAlreadyExists:
                return "방 이름이 이미 존재합니다";
            case ShutdownReason.GameIsFull:
                return "방이 가득 찼습니다";
            case ShutdownReason.InvalidAuthentication:
                return "인증 실패";
            case ShutdownReason.CustomAuthenticationFailed:
                return "사용자 인증 실패";
            case ShutdownReason.AuthenticationTicketExpired:
                return "인증 티켓 만료";
            case ShutdownReason.PhotonCloudTimeout:
                return "클라우드 연결 시간 초과";
            default:
                return reason.ToString();
        }
    }
}
