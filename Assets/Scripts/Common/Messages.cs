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
                return "���� ����";
            case ShutdownReason.Error:
                return "�� �� ���� ����";
            case ShutdownReason.ServerInRoom:
                return "������ �̹� �ٸ� �濡 �ֽ��ϴ�";
            case ShutdownReason.DisconnectedByPluginLogic:
                return "�÷����� ������ ���� ���� ����";
            case ShutdownReason.GameClosed:
                return "������ ����Ǿ����ϴ�";
            case ShutdownReason.GameNotFound:
                return "���� ã�� �� �����ϴ�";
            case ShutdownReason.MaxCcuReached:
                return "�ִ� ���� ������ �� ����";
            case ShutdownReason.InvalidRegion:
                return "�߸��� ����";
            case ShutdownReason.GameIdAlreadyExists:
                return "�� �̸��� �̹� �����մϴ�";
            case ShutdownReason.GameIsFull:
                return "���� ���� á���ϴ�";
            case ShutdownReason.InvalidAuthentication:
                return "���� ����";
            case ShutdownReason.CustomAuthenticationFailed:
                return "����� ���� ����";
            case ShutdownReason.AuthenticationTicketExpired:
                return "���� Ƽ�� ����";
            case ShutdownReason.PhotonCloudTimeout:
                return "Ŭ���� ���� �ð� �ʰ�";
            default:
                return reason.ToString();
        }
    }
}
