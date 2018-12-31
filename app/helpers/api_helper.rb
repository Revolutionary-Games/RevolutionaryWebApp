module ApiHelper
  
  def self.check_token(token, access: :basic)

    user = User.find_by(api_token: token)

    if !user 
      return false
    end

    if access == :developer
      if !user.developer
        return false
      end
    end

    return true
  end

  # Parses symbol definition from breakpad data
  # call like `platform, arch, hash, name = getBreakpadSymbolInfo data`
  def self.getBreakpadSymbolInfo(data)
    match = data.match(/MODULE\s(\w+)\s(\w+)\s(\w+)\s(\S+)/i)

    if !match || match.captures.length != 4
      raise "invalid breakpad data"
    end

    match.captures
  end
  
  
end

