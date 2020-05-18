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

  # Non-rails load of acting_user
  def self.acting_user_from_session_id(id)
    session = ActiveRecord::SessionStore::Session.find_by session_id: id

    return nil unless session

    ApplicationHelper.acting_user_from_session session.data.symbolize_keys
  end

  # Parses symbol definition from breakpad data
  # call like `platform, arch, hash, name = getBreakpadSymbolInfo data`
  def self.getBreakpadSymbolInfo(data)
    match = data.match(/MODULE\s(\w+)\s(\w+)\s(\w+)\s(\S+)/i)

    raise 'invalid breakpad data' if !match || match.captures.length != 4

    match.captures
  end
end
