# frozen_string_literal: true

module ApiHelper
  def self.check_token(token, access: :basic)
    user = User.find_by(api_token: token)

    return false unless user

    if access == :developer
      return false unless user.developer
    end

    true
  end

  def self.get_user_from_api_token(token)
    return nil if token.nil?

    user = User.find_by api_token: token

    return nil unless user

    # TODO: check for suspended

    user
  end

  # Parses symbol definition from breakpad data
  # call like `platform, arch, hash, name = getBreakpadSymbolInfo data`
  def self.getBreakpadSymbolInfo(data)
    match = data.match(/MODULE\s(\w+)\s(\w+)\s(\w+)\s(\S+)/i)

    raise 'invalid breakpad data' if !match || match.captures.length != 4

    match.captures
  end
end
