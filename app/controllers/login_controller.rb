# frozen_string_literal: true

# This handles the POSTed login form coming from a hyperstack component
class LoginController < ApplicationController
  include PatreonGroupHelper

  def failed; end

  def do_login
    # SSO
    if !params[:sso_type].blank?
      unless ENV['BASE_URL']
        @error = 'Invalid server configuration. Missing BASE_URL'
        return
      end

      if params[:sso_type] == 'devforum'
        unless ENV['DEV_FORUM_SSO_SECRET']
          @error = 'Invalid server configuration. Missing DEV_FORUM_SSO_SECRET'
          return
        end

        return_url = URI.join(ENV['BASE_URL'], '/login/sso_return').to_s

        payload = prepare_discourse_login return_url

        signature = OpenSSL::HMAC.hexdigest('SHA256', ENV['DEV_FORUM_SSO_SECRET'], payload)

        encoded = CGI.escape payload

        redirect_to URI.join('https://forum.revolutionarygamesstudio.com/',
                             "/session/sso_provider?sso=#{encoded}&sig=#{signature}").to_s
      elsif params[:sso_type] == 'communityforum'
        unless ENV['COMMUNITY_FORUM_SSO_SECRET']
          @error = 'Invalid server configuration. Missing COMMUNITY_FORUM_SSO_SECRET'
          return
        end

        return_url = URI.join(ENV['BASE_URL'], '/login/sso_return_community').to_s

        payload = prepare_discourse_login return_url

        signature = OpenSSL::HMAC.hexdigest('SHA256', ENV['COMMUNITY_FORUM_SSO_SECRET'],
                                            payload)

        encoded = CGI.escape payload

        redirect_to URI.join('https://community.revolutionarygamesstudio.com/',
                             "/session/sso_provider?sso=#{encoded}&sig=#{signature}").to_s
      elsif params[:sso_type] == 'patreon'

        unless ENV['PATREON_LOGIN_CLIENT_SECRET'] && ENV['PATREON_LOGIN_CLIENT_ID']
          @error = 'Invalid server configuration. Missing PATREON_LOGIN_CLIENT_SECRET ' \
                   'or PATREON_LOGIN_CLIENT_ID'
          return
        end

        return_url = patreon_login_redirect

        id = ENV['PATREON_LOGIN_CLIENT_ID']

        setup_sso_nonce

        scopes = CGI.escape 'identity identity[email]'

        redirect_to 'https://www.patreon.com/oauth2/authorize?response_type=code&' \
                    "client_id=#{id}&redirect_uri=#{return_url}&scope=#{scopes}&" \
                    "state=#{session[:sso_nonce]}"
      else
        @error = 'Invalid SSO login type selected'
        return
      end
    else
      # local
      if !params[:email] || !params[:password] ||
         params[:email].blank? || params[:password].blank?

        redirect_to action: 'failed'
        return
      end

      user = User.find_by(email: params[:email]).try(:authenticate, params[:password])

      unless user

        redirect_to action: 'failed'
        return
      end

      # Succeeded
      session[:current_user_id] = user.id
      redirect_to '/'
    end
  end

  def sso_return
    handle_discourse_return :dev
  end

  def sso_return_community
    handle_discourse_return :community

    render action: 'sso_return' unless performed?
  end

  def sso_return_patreon
    handle_patreon_return

    render action: 'sso_return' unless performed?
  end

  def logout
    reset_session
    redirect_to '/login'
  end

  def check_sso_timeout
    if !session[:sso_start_time] || Time.current - session[:sso_start_time] > 20.minutes ||
       !session[:sso_nonce]
      @error = 'SSO request timed out. Please try again.'
      return false
    end
    true
  end

  def handle_discourse_return(type)
    if !params[:sig] || !params[:sso]
      @error = 'Missing URL query parameters'
      return
    end

    return unless check_sso_timeout

    secret = nil
    type_name = nil
    is_developer = nil

    if type == :dev
      unless ENV['DEV_FORUM_SSO_SECRET']
        @error = 'Invalid server configuration. missing DEV_FORUM_SSO_SECRET'
        return
      end

      secret = ENV['DEV_FORUM_SSO_SECRET']
      type_name = 'devforum'
      is_developer = true

    elsif type == :community
      unless ENV['COMMUNITY_FORUM_SSO_SECRET']
        @error = 'Invalid server configuration. missing COMMUNITY_FORUM_SSO_SECRET'
        return
      end

      secret = ENV['COMMUNITY_FORUM_SSO_SECRET']
      type_name = 'communityforum'
      is_developer = false

    else
      @error = 'Invalid discourse SSO type'
      return
    end

    returnedSignature = OpenSSL::HMAC.hexdigest('SHA256', secret, params[:sso])

    if returnedSignature != params[:sig]
      @error = 'Invalid SSO parameters'
      return
    end

    ssoParams = Rack::Utils.parse_nested_query Base64.decode64(params[:sso])

    if ssoParams['nonce'] != session[:sso_nonce]
      @error = 'Invalid SSO parameters'
      return
    end

    email = ssoParams['email']

    if email.blank?
      @error = 'Invalid returned account details. Email is empty'
      return
    end

    # Clear nonce to prevent duplicate attempts
    session[:sso_nonce] = ''

    if type == :community
      # Need to be in the supporter or vip supporter group
      if !ssoParams['groups'].include?(COMMUNITY_DEVBUILD_GROUP) &&
         !ssoParams['groups'].include?(COMMUNITY_VIP_GROUP)
        @error = 'You must be either in the Supporter or VIP supporter group to login. ' \
                 'These are granted to our Patrons'
        logger.info "Not allowing login due to missing group membership for: #{email}"
        return
      end
    end

    logger.info "Logging in user with email #{email}"

    # Detect account to log into or create one
    user = User.find_by email: ssoParams['email']

    if !user
      name = nil

      name = ssoParams['username'] if ssoParams['username']

      user = create_new_user email, type_name, name, is_developer

      return unless user

    elsif user.local
      # Disallow logging into local accounts
      return set_local_login_error
    elsif user.sso_source != type_name
      # Make sure that login is allowed
      logger.info "User logged in with different sso_source, new: #{type_name}, " \
                  "old: #{user.sso_source}"

      if user.sso_source == 'devforum' || user.developer
        @error = 'Your account is a developer account. ' \
                 "You can't login through the community forums"
        return
      end

      if type == :dev
        # Update to be a developer
        logger.info "User (#{user.email}) is now a developer"

        user.developer = true
        user.sso_source = type_name

        unless user.save
          @error = 'Failed to upgrade your account to a developer account. User saving failed.'
          return
        end
      else
        logger.info 'Patron logged in using forum account'
      end
    end

    # Success
    session[:current_user_id] = user.id
    redirect_to '/'
  end

  def handle_patreon_return
    return unless check_sso_timeout

    if params[:error]
      @error = "Patreon returned an error: #{params[:error]}"
      return
    end

    # Check nonce matches state
    if params[:state] != session[:sso_nonce] && !params[:code]
      @error = 'Invalid SSO parameters'
      return
    end

    # Clear nonce to prevent duplicate attempts
    session[:sso_nonce] = ''

    # Fetch the access tokens from patreon
    begin
      access, _refresh, _expires = PatreonAPI.turn_code_into_tokens(
        params[:code],
        ENV['PATREON_LOGIN_CLIENT_ID'],
        ENV['PATREON_LOGIN_CLIENT_SECRET'],
        patreon_login_redirect
      )
    rescue StandardError => e
      @error = 'Token request to Patreon failed'
      logger.info "Failed response for token granting: #{e}"
      return
    end

    # Load user data from patreon
    begin
      user_info = PatreonAPI.get_logged_in_user_details access
    rescue StandardError => e
      @error = 'Requesting user data from Patreon failed'
      logger.info "Request to patreon user data failed: #{e}, #{e.response}"
      return
    end

    attributes = user_info['data']['attributes']

    # Check if the user is our patron
    patron = Patron.find_by email: attributes['email']

    patreon_settings = PatreonSettings.first

    if !patron
      @error = "You aren't a patron of Thrive Game according to our latest information. "\
               'You need to be at least at the dev builds level to login. '\
               'Please become our patron and try again'
      return
    elsif patron.suspended
      @error = "Your Patron status is currently suspended. Reason: #{patron.suspended_reason}"
      return
    elsif patron.pledge_amount_cents < patreon_settings.devbuilds_pledge_cents
      more = patreon_settings.devbuilds_pledge_cents - patron.pledge_amount_cents
      @error = 'Your current pledge is lower than the devbuilds level. You need to pledge: '\
               "#{more} more cents"
      return
    end

    logger.info "Patron (#{attributes['email']}) logging in"

    # Allow logging in (unless a developer (or local) account exists with the same email

    email = patron.alias_or_email

    user = User.find_by email: email

    type_name = 'patreon'

    if !user
      user = create_new_user email, sso_type, patron.username, false

      return unless user
    elsif user.local
      # Disallow logging into local accounts
      return set_local_login_error
    elsif user.sso_source != type_name
      # Make sure that login is allowed
      logger.info "User logged in with different sso_source, new: #{type_name}, " \
                  "old: #{user.sso_source}"

      if user.sso_source == 'devforum' || user.developer
        @error = 'Your account is a developer account. ' \
                 "You can't login through patreon"
        return
      elsif user.sso_source == 'communityforum'
        logger.info 'Community forum user logged in using Patren'
      else
        @error = 'Unhandled login case'
        return
      end
    end

    # Store the token as a proof that the user logged in using
    # patreon, we don't use it for anything else currently
    patron.patreon_token = access
    patron.save

    # Success
    session[:current_user_id] = user.id
    redirect_to '/'
  end

  def patreon_login_redirect
    URI.join(ENV['BASE_URL'], '/login/patreon').to_s
  end

  def create_new_user(email, sso_type, username, is_developer)
    logger.info "First time login. Creating new user: #{email}"

    user = User.create email: email, local: false, sso_source: sso_type,
                       developer: is_developer, name: username

    unless user.valid?
      logger.error('validation failures: ' + user.errors.full_messages.join('.\n'))
      @error = 'Creating User on first time login failed'
      return
    end

    unless user.save
      @error = "User saving failed. This shouldn't happen"
      return
    end

    user
  end

  def set_local_login_error
    @error = 'This email is used by a local account. Use the local login option.'
  end

  def setup_sso_nonce
    # store nonce in user session
    session[:sso_nonce] = SecureRandom.base58(32)

    # And a time for timing out
    session[:sso_start_time] = Time.current
  end

  def prepare_discourse_login(return_url)
    setup_sso_nonce

    Base64.encode64 "nonce=#{session[:sso_nonce]}&return_sso_url=#{return_url}"
  end
end
