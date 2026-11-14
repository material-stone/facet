namespace Facet;

internal static class KoreanPlaces
{
    private static readonly Dictionary<string, string> Cities = new(StringComparer.OrdinalIgnoreCase) {
        ["Seoul"]="서울특별시",["Busan"]="부산광역시",["Pusan"]="부산광역시",["Daegu"]="대구광역시",["Incheon"]="인천광역시",
        ["Gwangju"]="광주광역시",["Daejeon"]="대전광역시",["Ulsan"]="울산광역시",["Sejong"]="세종특별자치시",
        ["Suwon"]="수원시",["Seongnam"]="성남시",["Goyang"]="고양시",["Yongin"]="용인시",["Bucheon"]="부천시",
        ["Ansan"]="안산시",["Anyang"]="안양시",["Namyangju"]="남양주시",["Hwaseong"]="화성시",["Pyeongtaek"]="평택시",
        ["Uijeongbu"]="의정부시",["Siheung"]="시흥시",["Paju"]="파주시",["Gimpo"]="김포시",["Gwangmyeong"]="광명시",
        ["Gunpo"]="군포시",["Icheon"]="이천시",["Yangju"]="양주시",["Osan"]="오산시",["Guri"]="구리시",
        ["Anseong"]="안성시",["Pocheon"]="포천시",["Uiwang"]="의왕시",["Hanam"]="하남시",["Yeoju"]="여주시",
        ["Dongducheon"]="동두천시",["Gwacheon"]="과천시",["Chuncheon"]="춘천시",["Wonju"]="원주시",["Gangneung"]="강릉시",
        ["Donghae"]="동해시",["Taebaek"]="태백시",["Sokcho"]="속초시",["Samcheok"]="삼척시",["Cheongju"]="청주시",
        ["Chungju"]="충주시",["Jecheon"]="제천시",["Cheonan"]="천안시",["Gongju"]="공주시",["Boryeong"]="보령시",
        ["Asan"]="아산시",["Seosan"]="서산시",["Nonsan"]="논산시",["Gyeryong"]="계룡시",["Dangjin"]="당진시",
        ["Jeonju"]="전주시",["Gunsan"]="군산시",["Iksan"]="익산시",["Jeongeup"]="정읍시",["Namwon"]="남원시",
        ["Gimje"]="김제시",["Mokpo"]="목포시",["Yeosu"]="여수시",["Suncheon"]="순천시",["Naju"]="나주시",
        ["Gwangyang"]="광양시",["Pohang"]="포항시",["Gyeongju"]="경주시",["Gimcheon"]="김천시",["Andong"]="안동시",
        ["Gumi"]="구미시",["Yeongju"]="영주시",["Yeongcheon"]="영천시",["Sangju"]="상주시",["Mungyeong"]="문경시",
        ["Gyeongsan"]="경산시",["Changwon"]="창원시",["Jinju"]="진주시",["Tongyeong"]="통영시",["Sacheon"]="사천시",
        ["Gimhae"]="김해시",["Miryang"]="밀양시",["Geoje"]="거제시",["Yangsan"]="양산시",["Jeju"]="제주시",["Seogwipo"]="서귀포시"
    };
    private static readonly Dictionary<string, string> Regions = new(StringComparer.OrdinalIgnoreCase) {
        ["11"]="서울특별시",["26"]="부산광역시",["27"]="대구광역시",["28"]="인천광역시",["29"]="광주광역시",
        ["30"]="대전광역시",["31"]="울산광역시",["36"]="세종특별자치시",["41"]="경기도",["42"]="강원특별자치도",
        ["43"]="충청북도",["44"]="충청남도",["45"]="전북특별자치도",["46"]="전라남도",["47"]="경상북도",
        ["48"]="경상남도",["49"]="제주특별자치도",["50"]="제주특별자치도",["51"]="강원특별자치도",["52"]="전북특별자치도"
    };

    internal static string Display(WeatherLocation location)
    {
        if (location.Area.Any(c => c is >= '\uAC00' and <= '\uD7A3')) return location.Area;
        string name = location.Area.Trim();
        foreach (string suffix in new[] {"-si","-gun","-gu"}) if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) name = name[..^suffix.Length];
        if (location.CountryCode is "KR" or "" && Cities.TryGetValue(name.Replace(" ",""), out string? city)) {
            if (name.Equals("Gwangju",StringComparison.OrdinalIgnoreCase) && location.RegionCode=="41") return "광주시";
            return city;
        }
        if (location.CountryCode=="KR" && Regions.TryGetValue(location.RegionCode, out string? region)) return region;
        return location.CountryCode switch { "KR"=>"대한민국", "JP"=>"일본", "US"=>"미국", "CN"=>"중국", "TW"=>"대만", "GB"=>"영국", "DE"=>"독일", "FR"=>"프랑스", "CA"=>"캐나다", "AU"=>"호주", _=>"현재 위치" };
    }
}
