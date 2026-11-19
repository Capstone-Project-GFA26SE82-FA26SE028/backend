using AI.Common.DTOs;

namespace AI.BLL.Interfaces;

public interface IChatContextToken
{
    ChatContextDTO Read(string token, VerifiedGuest guest);

    string Write(ChatContextDTO context, VerifiedGuest guest);
}
