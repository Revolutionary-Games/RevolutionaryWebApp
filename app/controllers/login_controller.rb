class LoginController < ApplicationController
  def failed
  end

  def do_login
    # SSO
    if !params[:sso_type].blank?

      if params[:sso_type] == "devforum"

        if !ENV["BASE_URL"] || !ENV["DEV_FORUM_SSO_SECRET"]
          @error = "Invalid server configuration. Missing BASE_URL or DEV_FORUM_SSO_SECRET"
          return
        end

        # store nonce in user session
        session[:sso_nonce] = SecureRandom.base58(32)

        # And a time for timing out
        session[:sso_start_time] = Time.current

        returnURL = URI::join(ENV["BASE_URL"], "/login/sso_return").to_s

        payload = Base64.encode64 "nonce=#{session[:sso_nonce]}&return_sso_url=#{returnURL}"
        signature = OpenSSL::HMAC.hexdigest('SHA256', ENV["DEV_FORUM_SSO_SECRET"], payload)

        urlEncodedPayload = CGI.escape payload

        redirect_to URI::join(
                      "https://forum.revolutionarygamesstudio.com/",
                      "/session/sso_provider?sso=#{urlEncodedPayload}&sig=#{signature}").to_s
      else
        @error = "Invalid SSO login type selected"
        return
      end
    else
      # local
      if !params[:email] || !params[:password] ||
         params[:email].blank? || params[:password].blank?

        redirect_to :action => "failed"
        return
      end

      user = User.find_by(email: params[:email]).try(:authenticate, params[:password])

      if !user

        redirect_to :action => "failed"
        return
      end

      # Succeeded
      session[:current_user_id] = user.id
      redirect_to "/"
    end
  end

  def sso_return

    if !params[:sig] || !params[:sso]
      @error = "Missing URL query parameters"
      return
    end

    if !ENV["DEV_FORUM_SSO_SECRET"]
      @error = "Invalid server configuration. DEV_FORUM_SSO_SECRET"
      return
    end

    if !session[:sso_start_time] || Time.current - session[:sso_start_time] > 15.minutes ||
       !session[:sso_nonce]
      @error = "SSO request timed out. Please try again."
      return
    end

    returnedSignature = OpenSSL::HMAC.hexdigest('SHA256', ENV["DEV_FORUM_SSO_SECRET"],
                                                params[:sso])

    if returnedSignature != params[:sig]
      @error = "Invalid SSO parameters"
      return
    end

    ssoParams = Rack::Utils.parse_nested_query Base64.decode64(params[:sso])

    if ssoParams["nonce"] != session[:sso_nonce]
      @error = "Invalid SSO parameters"
      return
    end

    email = ssoParams["email"]

    if email.blank?
      @error = "Invalid returned accoutn details. Email is empty"
      return
    end

    # Clear nonce to prevent duplicate attempts
    session[:sso_nonce] = ""

    logger.info "Logging in user with email #{email}"

    # Detect account to log into or create one
    user = User.find_by email: ssoParams["email"]

    if !user
      logger.info "First time login. Creating new user: #{email}"

      user = User.create email: email, local: false, sso_source: "devforum",
                         developer: true

      if ssoParams["username"]
        user.name = ssoParams["username"]
      end

      if !user.valid?
        logger.error("validation failures: " + user.errors.full_messages.join('.\n'));
        @error = "Creating User on first time login failed"
        return
      end

      if !user.save
        @error = "User saving failed. This shouldn't happen"
        return
      end
    else
      # Disallow logging into local accounts
      if user.local
        @error = "This email is used by a local account. Use the local login option."
        return
      end
    end

    # Success
    session[:current_user_id] = user.id
    redirect_to "/"
  end

  def logout
    reset_session
    redirect_to "/login"
  end
end
