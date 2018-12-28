class User < ApplicationRecord

  validates :email, presence: true, uniqueness: true, length: { maximum: 255 }
  validates :name, length: { maximum: 100 }, allow_nil: true
  
  has_secure_password validations: false
  validates :password, length: { minimum: 6 }, allow_nil: true
  validates :password, length: { maximum: 255 }, allow_nil: true
  validates :password, confirmation: true

  
  validate :local_or_sso

  def generate_api_token
    self.api_token = SecureRandom.base58(32)
  end

  private

  def local_or_sso

    if (!local && sso_source.blank?) || (local && !sso_source.blank?)
      errors[:base] << "User must be local or have SSO source set"
    end

    if local && password.blank? && !password_digest
      errors[:base] << "Local user must have password"
    end

    if !local && (!password.blank? || password_digest)
      errors[:base] << "SSO user may not have password"
    end
  end
end
